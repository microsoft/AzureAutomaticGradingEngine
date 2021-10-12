using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class GradeReportFunction
    {
        [FunctionName("GradeReportFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string assignment = req.Query["assignment"];
            bool isJson = req.Query.ContainsKey("json");
            bool isToday = req.Query.ContainsKey("today");

            var accumulateMarks = await CalculateMarks(log, context, assignment, isToday);
            
            if (isJson)
            {
                return new JsonResult(accumulateMarks);
            }
            
            try
            {
                await using var stream = new MemoryStream();
                WriteWorkbookToMemoryStream(accumulateMarks, stream);
                var content = stream.ToArray();
                return new FileContentResult(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
            catch (Exception ex)
            {
                return new OkObjectResult(ex);
            }
        }

        public static MemoryStream WriteWorkbookToMemoryStream(Dictionary<string, Dictionary<string, int>> accumulateMarks, MemoryStream stream)
        {
            using var workbook = new XLWorkbook();
            GenerateMarksheet(accumulateMarks, workbook);
            workbook.SaveAs(stream);
            return stream;
        }

        public static async Task<Dictionary<string, Dictionary<string, int>>> CalculateMarks(ILogger log, ExecutionContext context, string assignment, bool isToday)
        {
            CloudStorageAccount storageAccount = CloudStorage.GetCloudStorageAccount(context);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("testresult");

            var blobItems = await CloudStorage.ListBlobsFlatListing(container, assignment, log);

            var testResults = blobItems.Select(c => new
            {
                Email = ExtractEmail(c.Uri.ToString()),
                TestResult = GetTestResult(container, c),
                CreateTime = GetTestTime(container, c)
            });

            bool IsNotToday(DateTime date) => (new DateTime(date.Year, date.Month, date.Day)).Subtract(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day)).Days != 0;


            var accumulateMarks = testResults.Aggregate(new Dictionary<string, Dictionary<string, int>>(), (acc, item) =>
            {
                if (isToday && IsNotToday(item.CreateTime))
                {
                    return acc; //Skip it.
                }

                if (acc.ContainsKey(item.Email))
                {
                    var previous = acc[item.Email];
                    var current = item.TestResult;
                    //Key is testname and Value is list of (testname,mark)
                    var result = previous.Concat(current).GroupBy(d => d.Key)
                        .ToDictionary(d => d.Key, d => d.Sum(c => c.Value));
                    acc[item.Email] = result;
                    return acc;
                }

                acc.Add(item.Email, item.TestResult);
                return acc;
            });
            return accumulateMarks;
        }

        private static void GenerateMarksheet(Dictionary<string, Dictionary<string, int>> accumulateMarks, XLWorkbook workbook)
        {
            var worksheet =
            workbook.Worksheets.Add("Marks");
            worksheet.Cell(1, 1).Value = "Email";
            worksheet.Cell(1, 2).Value = "Total";

            var tests = new HashSet<string>();
            for (var i = 0; i < accumulateMarks.Count(); i++)
            {
                var student = accumulateMarks.ElementAt(i);
                worksheet.Cell(i + 2, 1).Value = student.Key;
                tests.UnionWith(student.Value.Keys.ToHashSet());
                worksheet.Cell(i + 2, 2).Value = student.Value.Values.Sum();
            }
            var testList = tests.ToList();
            testList.Sort();
            for (var j = 0; j < testList.Count(); j++)
            {
                var testName = testList.ElementAt(j);
                worksheet.Cell(1, j + 3).Value = testName;
                for (var i = 0; i < accumulateMarks.Count(); i++)
                {
                    var student = accumulateMarks.ElementAt(i);
                    worksheet.Cell(i + 2, j + 3).Value = student.Value.GetValueOrDefault(testName, 0);
                }
            }
        }

        public static string ExtractEmail(string content)
        {
            const string matchEmailPattern =
  @"(([\w-]+\.)+[\w-]+|([a-zA-Z]{1}|[\w-]{2,}))@"
  + @"((([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\."
  + @"([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])){1}|"
  + @"([a-zA-Z]+[\w-]+\.)+[a-zA-Z]{2,4})";

            Regex rx = new Regex(
              matchEmailPattern,
              RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Find matches.
            MatchCollection matches = rx.Matches(content);

            return matches[0].Value;

        }

        private static Dictionary<string, int> GetTestResult(CloudBlobContainer cloudBlobContainer, IListBlobItem item)
        {
            var blobName = item.Uri.ToString()[(cloudBlobContainer.Uri.ToString().Length + 1)..];
            CloudBlockBlob blob = cloudBlobContainer.GetBlockBlobReference(blobName);

            string rawXml = blob.DownloadTextAsync().Result;
            var result = ParseNUnitTestResult(rawXml);

            return result;
        }

        public static Dictionary<string, int> ParseNUnitTestResult(string rawXml)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);

            XmlNodeList testCases = xmlDoc.SelectNodes("/test-run/test-suite/test-suite/test-suite/test-case");

            var result = new Dictionary<string, int>();
            foreach (XmlNode node in testCases)
            {
                result.Add(node.Attributes["fullname"].Value, node.Attributes["result"].Value == "Passed" ? 1 : 0);
            }

            return result;
        }

        private static DateTime GetTestTime(CloudBlobContainer cloudBlobContainer, IListBlobItem item)
        {
            var blobName = item.Uri.ToString()[(cloudBlobContainer.Uri.ToString().Length + 1)..];
            CloudBlockBlob blob = cloudBlobContainer.GetBlockBlobReference(blobName);
            var task = blob.FetchAttributesAsync();
            task.Wait();
            Debug.Assert(blob.Properties.Created != null, "blob.Properties.Created != null");
            return blob.Properties.Created.Value.DateTime;
        }
    }
}
