using AzureGraderFunctionApp;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
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
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {

            var assignments = await context.CallActivityAsync<List<Assignment>>("GetAssignmentList", null);

            var outputs = new List<string>();
            Console.WriteLine(assignments.Count());
            var tasks = new Task<string>[assignments.Count()];
            for (int i = 0; i < assignments.Count(); i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                    "GradeAssignment",
                    assignments[i]);
            }

            await Task.WhenAll(tasks);

            // Replace "hello" with the name of your Durable Activity Function.
            //outputs.Add(await context.CallActivityAsync<string>("Hello", "Tokyo"));
            //outputs.Add(await context.CallActivityAsync<string>("Hello", "Seattle"));
            //outputs.Add(await context.CallActivityAsync<string>("Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
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
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("credentials");

            var blobItems = await CloudStorage.ListBlobsFlatListing(container, null, log);
            //https://graderesult.blob.core.windows.net/credentials/vnet
            var assignments = blobItems.Select(c => new Assignment
            {
                Name = c.Uri.ToString().Replace(".json", "").Split("/credentials/")[1],
                Context = GetAssignmentContext(container, c)
            });


            return assignments.ToList();
        }

        private static ClassContext GetAssignmentContext(CloudBlobContainer cloudBlobContainer, IListBlobItem item)
        {
            var blobName = item.Uri.ToString().Substring(cloudBlobContainer.Uri.ToString().Length + 1);
            CloudBlockBlob blob = cloudBlobContainer.GetBlockBlobReference(blobName);

            string rawJson = blob.DownloadTextAsync().Result;
            dynamic studentContext = JsonConvert.DeserializeObject(rawJson);

            var classContext = new ClassContext() { GraderUrl = studentContext.graderUrl, Students = studentContext.students.ToString() };

            return classContext;
        }

        [FunctionName("GradeAssignment")]
        public static async Task GradeAssignment([ActivityTrigger] Assignment assignment, ExecutionContext context,
ILogger log)
        {

            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(context);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("testresult");

            var blobItems = await CloudStorage.ListBlobsFlatListing(container, assignment.Name, log);

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
            var queryPair = new NameValueCollection();            
            queryPair.Add("credentials", student.credentials.ToString());
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
            using (var ms = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(ms))
                {                   
                    writer.Write(xml);
                    writer.Flush();
                    ms.Position = 0;
                    await blob.UploadFromStreamAsync(ms);
                }           
            }
        }


        [FunctionName("ScheduleGrader")]
        public static async Task ScheduleGrader(
            [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
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