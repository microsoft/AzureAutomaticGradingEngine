using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Cronos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using AzureAutomaticGradingEngineFunctionApp.Dao;
using AzureAutomaticGradingEngineFunctionApp.Poco;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static partial class ScheduleGraderFunction
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
            var assignments = await context.CallActivityAsync<List<AssignmentPoco>>(nameof(GetAssignmentList), isManual);

            log.LogInformation($"context {context.InstanceId} {context.IsReplaying} Assignment Count = '{assignments.Count}' ignoreCronExpression:{isManual} ");
            var classJobs = new List<ClassGradingJob>();
            for (var i = 0; i < assignments.Count; i++)
            {
                classJobs.Add(ToClassGradingJob(assignments[i], log));
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

            await AssignmentTasks(context, nameof(SaveAccumulatedMarkJson), assignments);

            log.LogInformation("Completed!");
        }



        public static async Task AssignmentTasks(IDurableOrchestrationContext context, string activity, List<AssignmentPoco> assignments)
        {
            var retryOptions = new RetryOptions(
                firstRetryInterval: TimeSpan.FromSeconds(5),
                maxNumberOfAttempts: 1);
            var task = new Task[assignments.Count];
            for (var i = 0; i < assignments.Count; i++)
            {
                task[i] = context.CallActivityWithRetryAsync(activity, retryOptions, assignments[i]);
            }
            await Task.WhenAll(task);
        }


        [FunctionName(nameof(GetAssignmentList))]
#pragma warning disable IDE0060 // Remove unused parameter
        public static List<AssignmentPoco> GetAssignmentList([ActivityTrigger] bool ignoreCronExpression, ExecutionContext executionContext, ILogger log
#pragma warning restore IDE0060 // Remove unused parameter
    )
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);

            var cloudTableClient = storageAccount.CreateCloudTableClient();
            var config = new Config(executionContext);

            var assignmentDao = new AssignmentDao(config, log);
            var labCredentialDao = new LabCredentialDao(config, log);
            var assignments = assignmentDao.GetAssignments();

            var now = DateTime.UtcNow;
            bool IsTriggered(Assignment assignment)
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

            if (!ignoreCronExpression)
                assignments = assignments.Where(IsTriggered).ToList();

            var results = new List<AssignmentPoco>();
            foreach (var assignment in assignments)
            {
                string graderUrl = assignment.GraderUrl;
                string project = assignment.PartitionKey;
                bool sendMarkEmailToStudents = assignment.SendMarkEmailToStudents.HasValue && assignment.SendMarkEmailToStudents.Value;
                var labCredentials = labCredentialDao.GetByProject(project);
                var students = labCredentials.Select(c => new
                {
                    email = c.RowKey,
                    credentials = new { appId = c.AppId, displayName = c.DisplayName, tenant = c.Tenant, password = c.Password }
                }).ToArray();


                results.Add(new AssignmentPoco
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


        public static ClassGradingJob ToClassGradingJob(AssignmentPoco assignment, ILogger log)
        {
            var graderUrl = assignment.Context.GraderUrl;
            dynamic students = JsonConvert.DeserializeObject(assignment.Context.Students);
            log.LogInformation(assignment.Name + ":" + (int)students.Count);
            return new ClassGradingJob() { assignment = assignment, graderUrl = graderUrl, students = students };
        }

        [FunctionName(nameof(RunAndSaveTestResult))]
        public static async Task RunAndSaveTestResult([ActivityTrigger] SingleGradingJob job, ExecutionContext context, ILogger log)
        {
            var container = CloudStorage.GetCloudBlobContainer(context, "testresult");

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
                log.LogInformation("Calling grader URL for email -> " + (job.student.email as string));
                var xml = await client.GetStringAsync(uri);     
                
                await CloudStorage.SaveTestResult(container, job.assignment.Name, job.student.email.ToString(), xml, job.assignment.GradeTime);
                if (job.assignment.SendMarkEmailToStudents)
                    EmailTestResultToStudent(context, log, job.assignment.Name, job.student.email.ToString(), xml, job.assignment.GradeTime);
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                log.LogInformation((job.student.email as string) + " get test result in " + elapsedMs + "ms.");
            }
            catch (Exception ex)
            {
                log.LogInformation((job.student.email as string) + " in error.");
                log.LogInformation(ex.ToString());
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

        private static void EmailTestResultToStudent(ExecutionContext context, ILogger log, string assignment, string email, string xml, DateTime gradeTime)
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
                Subject = $"Your {assignment} Mark at {gradeTime}",
                Body = body
            };

            var config = new Config(context);
            var emailClient = new Email(config, log);
            emailClient.Send(emailMessage, new[] { Email.StringToAttachment(xml, "TestResult.txt", "text/plain") });
        }

        [FunctionName(nameof(SaveAccumulatedMarkJson))]
        public static async Task SaveAccumulatedMarkJson([ActivityTrigger] AssignmentPoco assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var accumulatedMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, false);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/accumulatedMarks.json", assignment.GradeTime);
            await CloudStorage.SaveJsonReport(executionContext, blobName, accumulatedMarks);
            blobName = assignment.Name + "/accumulatedMarks.json";
            await CloudStorage.SaveJsonReport(executionContext, blobName, accumulatedMarks);

            var workbookMemoryStream = new MemoryStream();
            GradeReportFunction.WriteWorkbookToMemoryStream(accumulatedMarks, workbookMemoryStream);

            blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/marks.xlsx", assignment.GradeTime);
            await CloudStorage.SaveExcelReport(executionContext, blobName, workbookMemoryStream);
            blobName = assignment.Name + "/marks.xlsx";
            await CloudStorage.SaveExcelReport(executionContext, blobName, workbookMemoryStream);

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
    }
}