using System;
using System.Collections.Generic;
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
using Microsoft.WindowsAzure.Storage.Blob;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class GradeReportFunction
    {
        [FunctionName(nameof(GradeReportFunction))]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            log.LogInformation($@"C# HTTP trigger {nameof(GradeReportFunction)} function processed a request.");

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

        public static MemoryStream WriteWorkbookToMemoryStream(Dictionary<string, (Dictionary<string, int>, Dictionary<string, DateTime>)> accumulateMarks, MemoryStream stream)
        {
            using var workbook = new XLWorkbook();
            GenerateMarksheets(accumulateMarks, workbook);
            workbook.SaveAs(stream);
            return stream;
        }

        public static async Task<Dictionary<string, (Dictionary<string, int>, Dictionary<string, DateTime>)>> CalculateMarks(ILogger log, ExecutionContext context, string assignment, bool isToday)
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(context);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("testresult");

            var blobItems = await CloudStorage.ListBlobsFlatListing(container, assignment, log, isToday);

            var testResults = blobItems.Select(c => new
            {
                Email = ExtractEmail(c.Uri.ToString()),
                XmlDoc = LoadTestResultToXmlDocument(container, c),
            }).Select(c => new
            {
                c.Email,
                TestResult = ParseNUnitTestResult(c.XmlDoc),
                CreateTime = GetTestTime(c.XmlDoc)
            }).Select(c => new
            {
                c.Email,
                c.TestResult,
                TestCompeteTime = c.TestResult.Select(a => (a.Key, a.Value == 1 ? c.CreateTime : DateTime.MaxValue)).ToDictionary(d => d.Key, d => d.Item2),
                c.CreateTime
            }); ;

            bool IsNotToday(DateTime date) => (new DateTime(date.Year, date.Month, date.Day)).Subtract(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day)).Days != 0;


            var accumulateMarks = testResults.Aggregate(new Dictionary<string, (Dictionary<string, int>, Dictionary<string, DateTime>)>(), (acc, item) =>
            {
                if (isToday && IsNotToday(item.CreateTime))
                {
                    return acc; //Skip it.
                }
                if (acc.ContainsKey(item.Email))
                {
                    var previous = acc[item.Email];
                    var currentMark = item.TestResult;
                    //Key is testname and Value is list of (testname,mark)
                    var markResult = previous.Item1.Concat(currentMark).GroupBy(d => d.Key)
                        .ToDictionary(d => d.Key, d => d.Sum(c => c.Value));

                    var currentTime = item.TestCompeteTime;
                    var timeResult = previous.Item2.Concat(currentTime).GroupBy(d => d.Key)
                        .ToDictionary(d => d.Key, d => d.Min(c => c.Value));

                    acc[item.Email] = (markResult, timeResult);
                    return acc;
                }
                else
                {
                    acc.Add(item.Email, (item.TestResult, item.TestCompeteTime));
                    return acc;
                }
            });
            return accumulateMarks;
        }

        private static void GenerateMarksheets(Dictionary<string, (Dictionary<string, int>, Dictionary<string, DateTime>)> accumulateMarks, XLWorkbook workbook)
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
                tests.UnionWith(student.Value.Item1.Keys.ToHashSet());
                worksheet.Cell(i + 2, 2).Value = student.Value.Item1.Values.Sum();
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
                    worksheet.Cell(i + 2, j + 3).Value = student.Value.Item1.GetValueOrDefault(testName, 0);
                }
            }

            worksheet =
                workbook.Worksheets.Add("Time");
            worksheet.Cell(1, 1).Value = "Email";
            worksheet.Cell(1, 2).Value = "Begin at";

            tests = new HashSet<string>();
            for (var i = 0; i < accumulateMarks.Count(); i++)
            {
                var student = accumulateMarks.ElementAt(i);
                worksheet.Cell(i + 2, 1).Value = student.Key;
                tests.UnionWith(student.Value.Item2.Keys.ToHashSet());
                var beginDateTime = student.Value.Item2.Values.Min(c => c);
                if (beginDateTime == DateTime.MaxValue)
                    worksheet.Cell(i + 2, 2).Value = "";
                else
                    worksheet.Cell(i + 2, 2).Value = beginDateTime;
            }
            testList = tests.ToList();
            testList.Sort();
            for (var j = 0; j < testList.Count(); j++)
            {
                var testName = testList.ElementAt(j);
                worksheet.Cell(1, j + 3).Value = testName;
                for (var i = 0; i < accumulateMarks.Count(); i++)
                {
                    var student = accumulateMarks.ElementAt(i);
                    var completeTime = student.Value.Item2.GetValueOrDefault(testName, DateTime.MaxValue);
                    if (completeTime == DateTime.MaxValue)
                        worksheet.Cell(i + 2, j + 3).Value = "";
                    else
                        worksheet.Cell(i + 2, j + 3).Value = completeTime;
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
            var xmlDoc = LoadTestResultToXmlDocument(cloudBlobContainer, item);
            return ParseNUnitTestResult(xmlDoc);
        }

        public static Dictionary<string, int> ParseNUnitTestResult(string rawXml)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);
            return ParseNUnitTestResult(xmlDoc);
        }

        private static Dictionary<string, int> ParseNUnitTestResult(XmlDocument xmlDoc)
        {
            var testCases = xmlDoc.SelectNodes("/test-run/test-suite/test-suite/test-suite/test-case");
            var result = new Dictionary<string, int>();
            foreach (XmlNode node in testCases)
            {
                result.Add(node.Attributes?["fullname"].Value, node.Attributes?["result"].Value == "Passed" ? 1 : 0);
            }

            return result;
        }

        private static DateTime GetTestTime(XmlDocument xmlDoc)
        {
            //ISO 8601 pattern "2021-10-02T10:01:57.1589935Z"
            var testStartTime = DateTime.Parse(xmlDoc.SelectSingleNode("/test-run")?.Attributes?["start-time"].Value);
            //Ignore Second.
            testStartTime = testStartTime.AddSeconds(-testStartTime.Second);
            return testStartTime;
        }

        private static XmlDocument LoadTestResultToXmlDocument(CloudBlobContainer cloudBlobContainer, IListBlobItem item)
        {
            var blobName = item.Uri.ToString()[(cloudBlobContainer.Uri.ToString().Length + 1)..];
            var blob = cloudBlobContainer.GetBlockBlobReference(blobName);
            string rawXml = blob.DownloadTextAsync().Result;
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);
            return xmlDoc;
        }
    }
}
