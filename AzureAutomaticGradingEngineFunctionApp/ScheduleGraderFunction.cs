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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class ScheduleGraderFunction
    {

        [FunctionName("ScheduleGraderFunction")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            var assignments = await context.CallActivityAsync<List<Assignment>>("GetAssignmentList", null);

            Console.WriteLine(assignments.Count());
            var tasks = new Task<string>[assignments.Count()];
            for (var i = 0; i < assignments.Count(); i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                    "GradeAssignment",
                    assignments[i]);
            }
            await Task.WhenAll(tasks);

            tasks = new Task<string>[assignments.Count()];
            for (var i = 0; i < assignments.Count(); i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                    "SaveMarkJson",
                    assignments[i].Name);
            }
            await Task.WhenAll(tasks);


            Console.WriteLine("Completed!");
        }

        public class Assignment
        {
            public string Name { get; set; }
            public ClassContext Context { get; set; }
        }

        public class ClassContext
        {
            public string GraderUrl { get; set; }
            public string Students { get; set; }
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
            var assignments = new List<DynamicTableEntity>();
            do
            {
                var queryResult = await assignmentsTable.ExecuteQuerySegmentedAsync(new TableQuery(), token);
                assignments.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            var results = new List<Assignment>();
            foreach (var assignment in assignments)
            {
                string graderUrl = assignment.Properties["GraderUrl"].StringValue;
                string project = assignment.PartitionKey;

                var studentDynamic = new List<DynamicTableEntity>();
                do
                {
                    var queryResult = await credentialsTable.ExecuteQuerySegmentedAsync(
                        new TableQuery().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, project)), token);
                    studentDynamic.AddRange(queryResult.Results);
                    token = queryResult.ContinuationToken;
                } while (token != null);


                var students = studentDynamic.Select(c => new
                {
                    email = c.RowKey,
                    credentials = c.Properties["Credentials"].StringValue
                }).ToArray();


                results.Add(new Assignment
                {
                    Name = project,
                    Context = new ClassContext() { GraderUrl = graderUrl, Students = JsonConvert.SerializeObject(students) }
                });

            }
            return results;
        }


        [FunctionName("GradeAssignment")]
        public static async Task GradeAssignment([ActivityTrigger] Assignment assignment, ExecutionContext context,
ILogger log)
        {

            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(context);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("testresult");

            string graderUrl = assignment.Context.GraderUrl;
            dynamic students = JsonConvert.DeserializeObject(assignment.Context.Students);

            Console.WriteLine(assignment.Name + ":" + students.Count);
            foreach (dynamic student in students)
            {
                //TODO: Back to parallel call when found the solution to run Nunit test in parallel in Azure Function.
                await RunAndSaveTestResult(assignment, container, graderUrl, student);
            }
        }

        private static async Task RunAndSaveTestResult(Assignment assignment, CloudBlobContainer container, string graderUrl, dynamic student)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(3);
            var queryPair = new NameValueCollection();
            queryPair.Set("credentials", student.credentials.ToString());
            queryPair.Set("trace", student.email.ToString());

            var uri = new Uri(graderUrl + ToQueryString(queryPair));
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var xml = await client.GetStringAsync(uri);
                await SaveTestResult(container, assignment.Name, student.email.ToString(), xml);
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                Console.WriteLine(student.email + " get test result in " + elapsedMs + "ms.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(student.email + " in error.");
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

        private static async Task SaveTestResult(CloudBlobContainer container, string assignment, string email, string xml)
        {
            var filename = Regex.Replace(email, @"[^0-9a-zA-Z]+", "");
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/" + email + "/{0:yyyy/MM/dd/HH/mm}/" + filename + ".xml", DateTime.Now);
            Console.WriteLine(blobName);

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            blob.Properties.ContentType = "application/xml";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(xml);
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        [FunctionName("SaveMarkJson")]
        public static async Task SaveMarkJson([ActivityTrigger] string assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var now = DateTime.Now;
            var accumulatedMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment, false);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/{0:yyyy/MM/dd/HH/mm}/accumulatedMarks.json", now);
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);
            blobName = assignment + "/accumulatedMarks.json";
            await SaveJsonReport(executionContext, blobName, accumulatedMarks);
            var todayMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment, true);
            blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/{0:yyyy/MM/dd/HH/mm}/todayMarks.json", now);
            await SaveJsonReport(executionContext, blobName, todayMarks);
            blobName = assignment + "/todayMarks.json";
            await SaveJsonReport(executionContext, blobName, todayMarks);
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


        [FunctionName("ScheduleGrader")]
        public static async Task ScheduleGrader(
            [TimerTrigger("0 */60 * * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (myTimer.IsPastDue)
            {
                log.LogInformation("Timer is running late!");
            }
            string instanceId = await starter.StartNewAsync("ScheduleGraderFunction", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName("ManualGrader")]
        public static async Task ManualGrader(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context,
            [DurableClient] IDurableOrchestrationClient starter
           )
        {
            var instanceId = await starter.StartNewAsync("ScheduleGraderFunction", null);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
    }
}