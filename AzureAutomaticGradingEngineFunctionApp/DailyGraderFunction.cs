using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
    }
}
