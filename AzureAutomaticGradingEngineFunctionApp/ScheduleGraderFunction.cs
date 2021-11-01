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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class ScheduleGraderFunction
    {
        [FunctionName("ScheduleGrader")]
        public static async Task ScheduleGrader(
            [TimerTrigger("0 0 */12 * * *")] TimerInfo myTimer,
            //[TimerTrigger("0 0 * * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (myTimer.IsPastDue)
            {
                log.LogInformation("Timer is running late!");
            }
            string instanceId = await starter.StartNewAsync("GraderOrchestrationFunction", null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName("ManualRunGraderOrchestrationFunction")]
        public static async Task<IActionResult> ManualRunGraderOrchestrationFunction(
#pragma warning disable IDE0060 // Remove unused parameter
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context,
#pragma warning restore IDE0060 // Remove unused parameter
            [DurableClient] IDurableOrchestrationClient starter
        )
        {
            var instanceId = await starter.StartNewAsync("GraderOrchestrationFunction", null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return new ContentResult
            {
                Content = $"Started orchestration with ID = '{instanceId}'.",
                ContentType = "text/html",
                StatusCode = 200,
            };
        }

        [FunctionName("GraderOrchestrationFunction")]
        public static async Task GraderOrchestrationFunction(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var assignments = await context.CallActivityAsync<List<Assignment>>("GetAssignmentList", null);

            Console.WriteLine(assignments.Count());
            var classJobs = new List<ClassGradingJob>();
            for (var i = 0; i < assignments.Count(); i++)
            {
                classJobs.Add(ToClassGradingJob(assignments[i]));
            }

            foreach (var classGradingJob in classJobs)
            {

                // Parallel mode code is working due to Azure Function cannot run NUnit in parallel.
                var gradingTasks = new Task[classGradingJob.students.Count];
                var i = 0;
                foreach (dynamic student in classGradingJob.students)
                {
                    gradingTasks[i] = context.CallActivityAsync(
                        "RunAndSaveTestResult",
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

            var task2s = new Task[assignments.Count()];
            for (var i = 0; i < assignments.Count(); i++)
            {
                task2s[i] = context.CallActivityAsync(
                    "SaveMarkJson",
                    assignments[i]);
            }
            await Task.WhenAll(task2s);


            Console.WriteLine("Completed!");
        }


        [FunctionName("GetAssignmentList")]
#pragma warning disable IDE0060 // Remove unused parameter
        public static async Task<List<Assignment>> GetAssignmentList([ActivityTrigger] string name, ExecutionContext executionContext
#pragma warning restore IDE0060 // Remove unused parameter
    )
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);

            var cloudTableClient = storageAccount.CreateCloudTableClient();
            var assignmentsTable = cloudTableClient.GetTableReference("assignments");
            var credentialsTable = cloudTableClient.GetTableReference("credentials");

            TableContinuationToken token = null;
            var assignments = new List<AssignmentTableEntity>();
            do
            {
                var queryResult = await assignmentsTable.ExecuteQuerySegmentedAsync(new TableQuery<AssignmentTableEntity>(), token);
                assignments.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

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

        [FunctionName("RunAndSaveTestResult")]
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

            var marks = String.Join("",
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
        
        [FunctionName("SaveMarkJson")]
        public static async Task SaveMarkJson([ActivityTrigger] Assignment assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var now = DateTime.Now;
            var accumulatedMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, false);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/accumulatedMarks.json", now);
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);
            blobName = assignment.Name + "/accumulatedMarks.json";
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);
            var todayMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, true);
            blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/todayMarks.json", now);
            await SaveJsonReport(executionContext, blobName, todayMarks);
            blobName = assignment.Name + "/todayMarks.json";
            await SaveJsonReport(executionContext, blobName, todayMarks);

            var workbookMemoryStream = new MemoryStream();
            GradeReportFunction.WriteWorkbookToMemoryStream(accumulatedMarks, workbookMemoryStream);

            blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/marks.json", now);
            await SaveExcelReport(executionContext, blobName, workbookMemoryStream);
            blobName = assignment.Name + "/marks.xlsx";
            await SaveExcelReport(executionContext, blobName, workbookMemoryStream);

            if (!string.IsNullOrEmpty(assignment.TeacherEmail))
            {
                var emailMessage = new EmailMessage
                {
                    To = assignment.TeacherEmail,
                    Subject = $"Accumulated Mark for {assignment.Name} at {now}",
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

        private static async Task SaveJsonReport(ExecutionContext executionContext, string blobName, Dictionary<string, Dictionary<string, int>> calculateMarks)
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