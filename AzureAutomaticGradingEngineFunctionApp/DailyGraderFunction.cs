using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static partial class ScheduleGraderFunction
    {
        [FunctionName(nameof(DailyGrader))]
        public static async Task DailyGrader(
            [TimerTrigger("0 0 0 * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (myTimer.IsPastDue)
            {
                log.LogInformation("Timer is running late!");
            }
            string instanceId = await starter.StartNewAsync(nameof(DailyGraderOrchestrationFunction), null, true);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName(nameof(DailyGraderOrchestrationFunction))]
        public static async Task DailyGraderOrchestrationFunction(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            var assignments = await context.CallActivityAsync<List<Assignment>>(nameof(GetAssignmentList), true);
            await ScheduleGraderFunction.AssignmentTasks(context, nameof(SaveTodayMarkJson), assignments);
            Console.WriteLine("Completed!");
        }

        [FunctionName(nameof(SaveTodayMarkJson))]
        public static async Task SaveTodayMarkJson([ActivityTrigger] Assignment assignment,
            ExecutionContext executionContext,
            ILogger log)
        {
            var todayMarks = await GradeReportFunction.CalculateMarks(log, executionContext, assignment.Name, true);
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment.Name + "/{0:yyyy/MM/dd/HH/mm}/todayMarks.json", assignment.GradeTime);
            await CloudStorage.SaveJsonReport(executionContext, blobName, todayMarks);
            blobName = assignment.Name + "/todayMarks.json";
            await CloudStorage.SaveJsonReport(executionContext, blobName, todayMarks);
        }
    }
}
