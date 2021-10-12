using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace AzureProjectGraderFunctionApp
{
    public static class GraderFunction
    {
        [FunctionName("AzureGraderFunction")]
        public static async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
             ILogger log, ExecutionContext context)
        {
            log.LogInformation("Start AzureGraderFunction");


            if (req.Method == "GET")
            {
                if (!req.Query.ContainsKey("credentials"))
                {
                    string html = @"
<!DOCTYPE html>

<html lang='en' xmlns='http://www.w3.org/1999/xhtml'>
<head>
    <meta charset='utf-8' />
    <title>Azure Grader</title>
</head>
<body>
    <form id='contact-form' method='post'>
        Azure Credentials<br/>
        <textarea name='credentials' required  rows='15' cols='100'></textarea>
        <br/>
        <button type='submit'>Run Test</button>
    </form>
</body>
</html>";


                    return new ContentResult()
                    {
                        Content = html,
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }
                else
                {
                    string credentials = req.Query["credentials"];

                    string xml;
                    if (req.Query.ContainsKey("trace"))
                    {
                        string trace = req.Query["trace"];
                        log.LogInformation("start:" + trace);
                        xml = await RunUnitTestProcess(context, log, credentials, trace);
                        log.LogInformation("end:" + trace);
                    }
                    else
                    {
                        xml = await RunUnitTestProcess(context, log, credentials);
                    }
                    return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
                }

            }
            else if (req.Method == "POST")
            {
                log.LogInformation("POST Request");
                string credentials = req.Form["credentials"];
                if (credentials == null)
                {
                    return new ContentResult
                    {
                        Content = $"<result><value>No credentials</value></result>",
                        ContentType = "application/xml",
                        StatusCode = 422
                    };
                }
                var xml = await RunUnitTestProcess(context, log, credentials);

                return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
            }

            return new OkObjectResult("ok");
        }

        private static async Task<string> RunUnitTestProcess(ExecutionContext context, ILogger log, string credentials, string trace = "NoTrace")
        {
            var tempDir = GetTemporaryDirectory(trace);
            var tempCredentialsFilePath = Path.Combine(tempDir, "azureauth.json");

            await File.WriteAllLinesAsync(tempCredentialsFilePath, new string[] { credentials });

            string workingDirectoryInfo = context.FunctionAppDirectory;
            string exeLocation = Path.Combine(context.FunctionAppDirectory, "AzureGraderTestProject.exe");

            try
            {
                using Process process = new Process();
                ProcessStartInfo info = new ProcessStartInfo
                {
                    WorkingDirectory = workingDirectoryInfo,
                    FileName = exeLocation,
                    Arguments = $@"{tempCredentialsFilePath} {tempDir} {trace}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.StartInfo = info;

                log.LogInformation("Refresh start.");
                process.Refresh();
                log.LogInformation("Process start.");
                var output = new StringBuilder();
                var error = new StringBuilder();
                using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
                using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            outputWaitHandle.Set();
                        }
                        else
                        {
                            output.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            errorWaitHandle.Set();
                        }
                        else
                        {
                            error.AppendLine(e.Data);
                        }
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var timeout = 5 * 60 * 1000;
                    if (process.WaitForExit(timeout) &&
                        outputWaitHandle.WaitOne(timeout) &&
                        errorWaitHandle.WaitOne(timeout))
                    {
                        // Process completed. Check process.ExitCode here.
                        log.LogInformation("Process Ended.");

                        log.LogInformation(error.ToString());
                        var xml = await File.ReadAllTextAsync(Path.Combine(tempDir, "TestResult.xml"));
                        Directory.Delete(tempDir, true);
                        return xml;
                    }
                    else
                    {
                        // Timed out.
                        log.LogInformation("Process Timed out.");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());
            }
            return null;
        }

        private static string GetTemporaryDirectory(string trace)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), Math.Abs(trace.GetHashCode()).ToString());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
    }
}
