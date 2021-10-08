using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
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
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.WindowsAzure.Storage.Queue;

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
            string instanceId = await starter.StartNewAsync("ScheduleGraderOrchestrationFunction", null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName("ManualRunScheduleGraderOrchestrationFunction")]
        public static async Task ManualGrader(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context,
            [DurableClient] IDurableOrchestrationClient starter
        )
        {
            var instanceId = await starter.StartNewAsync("ScheduleGraderOrchestrationFunction", null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName("ScheduleGraderOrchestrationFunction")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            var assignments = await context.CallActivityAsync<List<Assignment>>("GetAssignmentList", null);

            Console.WriteLine(assignments.Count());
            var classJobs = new List<ClassGradingJob>();
            for (var i = 0; i < assignments.Count(); i++)
            {
                classJobs.Add(GradeAssignment(assignments[i]));
            }

            foreach (var classGradingJob in classJobs)
            {
                foreach (dynamic student in classGradingJob.students)
                {
                    await context.CallActivityAsync(
                        "RunAndSaveTestResult",
                        new SingleGradingJob
                        {
                            assignment = classGradingJob.assignment,
                            graderUrl = classGradingJob.graderUrl,
                            student = student
                        });
                }

                // Parallel mode code is working due to Azure Function cannot run NUnit in parallel.
                //var gradingTasks = new Task<SingleGradingJob>[classGradingJob.students.Count];
                //var i = 0;
                //foreach (dynamic student in classGradingJob.students)
                //{
                //    gradingTasks[i] = context.CallActivityAsync<SingleGradingJob>(
                //        "RunAndSaveTestResult",
                //        new SingleGradingJob
                //        {
                //            assignment = classGradingJob.assignment,
                //            graderUrl = classGradingJob.graderUrl,
                //            student = student
                //        });
                //    i++;
                //}
                //await Task.WhenAll(gradingTasks);
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
        public static async Task<List<Assignment>> GetAssignmentList([ActivityTrigger] string name, ExecutionContext executionContext,
    ILogger log)
        {
            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);

            CloudTableClient cloudTableClient = storageAccount.CreateCloudTableClient();
            CloudTable assignmentsTable = cloudTableClient.GetTableReference("assignments");
            CloudTable credentialsTable = cloudTableClient.GetTableReference("credentials");

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
                bool SendMarkEmailToStudents = assignment.SendMarkEmailToStudents.HasValue && assignment.SendMarkEmailToStudents.Value;

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
                    SendMarkEmailToStudents = SendMarkEmailToStudents,
                    Context = new ClassContext() { GraderUrl = graderUrl, Students = JsonConvert.SerializeObject(students) }
                });

            }
            return results;
        }


        public static ClassGradingJob GradeAssignment(Assignment assignment)
        {
            string graderUrl = assignment.Context.GraderUrl;
            dynamic students = JsonConvert.DeserializeObject(assignment.Context.Students);
            Console.WriteLine(assignment.Name + ":" + students.Count);
            return new ClassGradingJob() { assignment = assignment, graderUrl = graderUrl, students = students };
        }

        [FunctionName("RunAndSaveTestResult")]
        public static async Task RunAndSaveTestResult([ActivityTrigger] SingleGradingJob job, ExecutionContext context, ILogger log)
        {
            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(context);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("testresult");

            var client = new HttpClient();
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
                    SendTestResultToStudent(context, log, job.assignment.Name, job.student.email.ToString(), xml, now);
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
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/" + email + "/{0:yyyy/MM/dd/HH/mm}/" + filename + ".content", now);
            Console.WriteLine(blobName);

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            blob.Properties.ContentType = "application/content";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(xml);
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        private static void SendTestResultToStudent(ExecutionContext context, ILogger log, string assignment, string email, string xml, DateTime now)
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
            emailClient.Send(emailMessage, new[] { StringToAttachment(xml, "TestResult.xml", "text/xml") });
        }

        private static Attachment StringToAttachment(string content, string name, string mediaType)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, leaveOpen: true);
            writer.Write(content);
            writer.Flush();
            ms.Position = 0;
            return new Attachment(new MemoryStream(ms.ToArray()), name, mediaType);
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
                var jsonAttachment = StringToAttachment(JsonConvert.SerializeObject(accumulatedMarks),
                    "accumulatedMarks.json", "application/json");
                email.Send(emailMessage, new[] { excelAttachment, jsonAttachment });
            }
        }

        private static async Task SaveJsonReport(ExecutionContext executionContext, string blobName, Dictionary<string, Dictionary<string, int>> calculateMarks)
        {
            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("report");
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
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
            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("report");
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            blob.Properties.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            excelMemoryStream.Position = 0;
            await blob.UploadFromStreamAsync(excelMemoryStream);
        }
    }
}