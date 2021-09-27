using AzureGraderFunctionApp;
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
using System.Threading.Tasks;
using System.Web;

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
            for (int i = 0; i < assignments.Count(); i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                    "GradeAssignment",
                    assignments[i]);
            }
            await Task.WhenAll(tasks);
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

            CloudTableClient tblclient = storageAccount.CreateCloudTableClient();
            CloudTable assignmentsTable = tblclient.GetTableReference("assignments");
            CloudTable credentialsTable = tblclient.GetTableReference("credentials");

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
                    var queryResult = await credentialsTable.ExecuteQuerySegmentedAsync(new TableQuery().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, project)), token);
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

            Console.WriteLine(assignment.Name);

            string graderUrl = assignment.Context.GraderUrl;
            dynamic students = JsonConvert.DeserializeObject(assignment.Context.Students);

            var tasks = new List<Task>();
            foreach (dynamic student in students)
            {
                var task = RunAndSaveTestResult(assignment, container, graderUrl, student);
                tasks.Add(task);
            }
            await Task.WhenAll();
        }

        private static async Task<string> RunAndSaveTestResult(Assignment assignment, CloudBlobContainer container, string graderUrl, dynamic student)
        {
            var client = new HttpClient();
            var queryPair = new NameValueCollection
            {
                { "credentials", student.credentials.ToString() }
            };
            var uri = new Uri(graderUrl + ToQueryString(queryPair));
            Console.WriteLine(uri);
            var xml = await client.GetStringAsync(uri);
            await SaveTestResult(container, assignment.Name, student.email.ToString(), xml);
            return xml;
        }

        private static string ToQueryString(NameValueCollection nvc)
        {
            var array = (
                from key in nvc.AllKeys
                from value in nvc.GetValues(key)
                select string.Format(
            "{0}={1}",
            HttpUtility.UrlEncode(key),
            HttpUtility.UrlEncode(value))
                ).ToArray();
            return "?" + string.Join("&", array);
        }

        private static async Task SaveTestResult(CloudBlobContainer container, string assignment, string email, string xml)
        {
            var prefix = string.Format(CultureInfo.InvariantCulture, assignment + "/" + email + "/{0:yyyy/MM/dd/HH/mm/}", DateTime.Now);

            CloudBlockBlob blob = container.GetBlockBlobReference(prefix + "TestResult.xml");
            blob.Properties.ContentType = "application/xml";
            using var ms = new MemoryStream();
            using StreamWriter writer = new StreamWriter(ms);
            writer.Write(xml);
            writer.Flush();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }


        [FunctionName("ScheduleGrader")]
        public static async Task ScheduleGrader(
            [TimerTrigger("0 */15 * * * *")] TimerInfo myTimer,
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
    }
}