using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Cronos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class ScheduleGraderFunction
    {
        [FunctionName(nameof(ScheduleGrader))]
        public static async Task ScheduleGrader(
            [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (myTimer.IsPastDue)
            {
                log.LogInformation("Timer is running late!");
            }
            string instanceId = await starter.StartNewAsync(nameof(GraderOrchestrationFunction), null, false);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName(nameof(ManualRunGraderOrchestrationFunction))]
        public static async Task<IActionResult> ManualRunGraderOrchestrationFunction(
#pragma warning disable IDE0060 // Remove unused parameter
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context,
#pragma warning restore IDE0060 // Remove unused parameter
            [DurableClient] IDurableOrchestrationClient starter
        )
        {

            var instanceId = await starter.StartNewAsync(nameof(GraderOrchestrationFunction), null, true);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(nameof(GraderOrchestrationFunction))]
        public static async Task GraderOrchestrationFunction(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            bool isManual = context.GetInput<bool>();
            var assignments = await context.CallActivityAsync<List<Assignment>>(nameof(GetAssignmentList), isManual);

            log.LogInformation($"context {context.InstanceId} {context.IsReplaying} Assignment Count = '{assignments.Count()}' isManual:{isManual} ");
            var classJobs = new List<ClassGradingJob>();
            for (var i = 0; i < assignments.Count(); i++)
            {
                classJobs.Add(ToClassGradingJob(assignments[i]));
            }
            var retryOptions = new RetryOptions(
                firstRetryInterval: TimeSpan.FromSeconds(5),
                maxNumberOfAttempts: 1);
            foreach (var classGradingJob in classJobs)
            {
                var gradingTasks = new Task[classGradingJob.students.Count];
                var i = 0;
                foreach (dynamic student in classGradingJob.students)
                {
                    gradingTasks[i] = context.CallActivityWithRetryAsync<Task>(
                        nameof(RunAndSaveTestResult), retryOptions,
                        new SingleGradingJob
                        {
                            assignment = classGradingJob.assignment,
                            graderUrl = classGradingJob.graderUrl,
                            student = student
                        });
                    i++;
                }
                await Task.WhenAll(gradingTasks);
            }


            async Task AssignmentTasks(string activity)
            {
                var task = new Task[assignments.Count()];
                for (var i = 0; i < assignments.Count(); i++)
                {
                    task[i] = context.CallActivityWithRetryAsync(activity, retryOptions, assignments[i]);
                }
                await Task.WhenAll(task);
            }

            await AssignmentTasks(nameof(SaveAccumulatedMarkJson));
            await AssignmentTasks(nameof(SaveTodayMarkJson));

            Console.WriteLine("Completed!");
        }


        [FunctionName(nameof(GetAssignmentList))]
#pragma warning disable IDE0060 // Remove unused parameter
        public static async Task<List<Assignment>> GetAssignmentList([ActivityTrigger] bool isManual, ExecutionContext executionContext, ILogger log
#pragma warning restore IDE0060 // Remove unused parameter
    )
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);

            var cloudTableClient = storageAccount.CreateCloudTableClient();
            var assignmentsTable = cloudTableClient.GetTableReference("assignments");
            await assignmentsTable.CreateIfNotExistsAsync();
            var credentialsTable = cloudTableClient.GetTableReference("credentials");
            await credentialsTable.CreateIfNotExistsAsync();

            TableContinuationToken token = null;
            var assignments = new List<AssignmentTableEntity>();
            do
            {
                var queryResult = await assignmentsTable.ExecuteQuerySegmentedAsync(new TableQuery<AssignmentTableEntity>(), token);
                assignments.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            var now = DateTime.UtcNow;
            bool IsTriggered(AssignmentTableEntity assignment)
            {
                try
                {
                    var expression = CronExpression.Parse(assignment.CronExpression);
                    var nextOccurrence = expression.GetNextOccurrence(now.AddSeconds(-10));
                    var diff = nextOccurrence.HasValue ? Math.Abs(nextOccurrence.Value.Subtract(now).TotalSeconds) : -1;
                    var trigger = nextOccurrence.HasValue && diff < 10;
                    log.LogInformation($"{assignment.PartitionKey} {assignment.CronExpression} trigger: {trigger} , diff: {diff} seconds");
                    return trigger;
                }
                catch (Exception)
                {
                    log.LogInformation($"{assignment.PartitionKey} Invalid Cron Expression {assignment.CronExpression}!");
                    return false;
                }
            }

            if (!isManual)
                assignments = assignments.Where(IsTriggered).ToList();

            var results = new List<Assignment>();
            foreach (var assignment in assignments)
            {
                string graderUrl = assignment.GraderUrl;
                string project = assignment.PartitionKey;
                bool sendMarkEmailToStudents = assignment.SendMarkEmailToStudents.HasValue && assignment.SendMarkEmailToStudents.Value;

                var credentialsTableEntities = new List<CredentialsTableEntity>();
                do
                {
                    var queryResult = await credentialsTable.ExecuteQuerySegmentedAsync(
                        new TableQuery<CredentialsTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, project)), token);
                    credentialsTableEntities.AddRange(queryResult.Results);
                    token = queryResult.ContinuationToken;
                } while (token != null);


                var students = credentialsTableEntities.Select(c => new
                {
                    email = c.RowKey,
                    credentials = c.Credentials
                }).ToArray();


                results.Add(new Assignment
                {
                    Name = project,
                    TeacherEmail = assignment.TeacherEmail,
                    SendMarkEmailToStudents = sendMarkEmailToStudents,
                    GradeTime = now,
                    Context = new ClassContext() { GraderUrl = graderUrl, Students = JsonConvert.SerializeObject(students) }
                });

            }
            return results;
        }


        public static ClassGradingJob ToClassGradingJob(Assignment assignment)
        {
            var graderUrl = assignment.Context.GraderUrl;
            dynamic students = JsonConvert.DeserializeObject(assignment.Context.Students);
            Console.WriteLine(assignment.Name + ":" + students.Count);
            return new ClassGradingJob() { assignment = assignment, graderUrl = graderUrl, students = students };
        }

        [FunctionName(nameof(RunAndSaveTestResult))]
        public static async Task RunAndSaveTestResult([ActivityTrigger] SingleGradingJob job, ExecutionContext context, ILogger log)
        {
            var container = GetCloudBlobContainer(context, "testresult");

#pragma warning disable IDE0017 // Simplify object initialization
            var client = new HttpClient();
#pragma warning restore IDE0017 // Simplify object initialization
            client.Timeout = TimeSpan.FromMinutes(3);
            var queryPair = new NameValueCollection();
            queryPair.Set("credentials", job.student.credentials.ToString());
            queryPair.Set("trace", job.student.email.ToString());

            var uri = new Uri(job.graderUrl + ToQueryString(queryPair));
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var xml = await client.GetStringAsync(uri);

                var now = DateTime.Now;
                await SaveTestResult(container, job.assignment.Name, job.student.email.ToString(), xml, now);
                if (job.assignment.SendMarkEmailToStudents)
                    EmailTestResultToStudent(context, log, job.assignment.Name, job.student.email.ToString(), xml, now);
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                Console.WriteLine(job.student.email + " get test result in " + elapsedMs + "ms.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(job.student.email + " in error.");
                Console.WriteLine(ex);
            }
        }

        private static string ToQueryString(NameValueCollection nvc)
        {
            var array = (
                from key in nvc.AllKeys
                from value in nvc.GetValues(key)
                select $"{HttpUtility.UrlEncode(key)}={HttpUtility.UrlEncode(value)}"
            ).ToArray();
            return "?" + string.Join("&", array);
        }

        private static async Task SaveTestResult(CloudBlobContainer container, string assignment, string email, string xml, DateTime now)
        {
            var filename = Regex.Replace(email, @"[^0-9a-zA-Z]+", "");
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/" + email + "/{0:yyyy/MM/dd/HH/mm}/" + filename + ".xml", now);
            Console.WriteLine(blobName);

            var blob = container.GetBlockBlobReference(blobName);
            blob.Properties.ContentType = "application/content";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(xml);
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        private static void EmailTestResultToStudent(ExecutionContext context, ILogger log, string assignment, string email, string xml, DateTime now)
        {
            var nUnitTestResult = GradeReportFunction.ParseNUnitTestResult(xml);
            var totalMark = nUnitTestResult.Sum(c => c.Value);

            var marks = string.Join("",
                nUnitTestResult.OrderBy(c => c.Key).Select(c => c.Key + ": " + c.Value + "\n").ToArray());

            var body = $@"
Dear Student,

You have just earned {totalMark} mark(s).

{marks}

Regards,
Azure Automatic Grading Engine
";
            var emailMessage = new EmailMessage
            {
                To = email,
                Subject = $"Your {assignment} Mark at {now}",
                Body = body
            };

            var config = new Config(context);
            var emailClient = new Email(config, log);
            emailClient.Send(emailMessage, new[] { Email.StringToAttachment(xml, "TestResult.txt", "text/plain") });
        }

        [FunctionName(nameof(SaveAccumulatedMarkJson))]
        public static async Task SaveAccumulatedMarkJson([ActivityTrigger] Assignment assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var now = DateTime.Now;
            var accumulatedMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, false);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/accumulatedMarks.json", now);
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);
            blobName = assignment.Name + "/accumulatedMarks.json";
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);

            var workbookMemoryStream = new MemoryStream();
            GradeReportFunction.WriteWorkbookToMemoryStream(accumulatedMarks, workbookMemoryStream);

            blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/marks.xlsx", now);
            await SaveExcelReport(executionContext, blobName, workbookMemoryStream);
            blobName = assignment.Name + "/marks.xlsx";
            await SaveExcelReport(executionContext, blobName, workbookMemoryStream);

            if (!string.IsNullOrEmpty(assignment.TeacherEmail))
            {
                var emailMessage = new EmailMessage
                {
                    To = assignment.TeacherEmail,
                    Subject = $"Accumulated Mark for {assignment.Name} on {assignment.GradeTime} (UTC)",
                    Body = @"Dear Teacher, 

Here are the accumulated mark report.

Regards,
Azure Automatic Grading Engine
"
                };

                var config = new Config(executionContext);
                var email = new Email(config, log);
                workbookMemoryStream = new MemoryStream(workbookMemoryStream.ToArray());
                var excelAttachment = new Attachment(workbookMemoryStream, "accumulatedMarks.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                var jsonAttachment = Email.StringToAttachment(JsonConvert.SerializeObject(accumulatedMarks),
                    "accumulatedMarks.json", "application/json");
                email.Send(emailMessage, new[] { excelAttachment, jsonAttachment });
            }
        }

        [FunctionName(nameof(SaveTodayMarkJson))]
        public static async Task SaveTodayMarkJson([ActivityTrigger] Assignment assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var now = DateTime.Now;
            var todayMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, true);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/todayMarks.json", now);
            await SaveJsonReport(executionContext, blobName, todayMarks);
            blobName = assignment.Name + "/todayMarks.json";
            await SaveJsonReport(executionContext, blobName, todayMarks);

        }

        private static async Task SaveJsonReport(ExecutionContext executionContext, string blobName, Dictionary<string, MarkDetails> calculateMarks)
        {
            var blob = GetCloudBlockBlobInReportContainer(executionContext, blobName);
            blob.Properties.ContentType = "application/json";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(JsonConvert.SerializeObject(calculateMarks));
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        private static async Task SaveExcelReport(ExecutionContext executionContext, string blobName, Stream excelMemoryStream)
        {
            var blob = GetCloudBlockBlobInReportContainer(executionContext, blobName);
            blob.Properties.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            excelMemoryStream.Position = 0;
            await blob.UploadFromStreamAsync(excelMemoryStream);
        }

        private static CloudBlockBlob GetCloudBlockBlobInReportContainer(ExecutionContext executionContext, string blobName)
        {
            var container = GetCloudBlobContainer(executionContext, "report");
            var blob = container.GetBlockBlobReference(blobName);
            return blob;
        }

        private static CloudBlobContainer GetCloudBlobContainer(ExecutionContext executionContext, string containerName)
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerName);
            return container;
        }
    }
}