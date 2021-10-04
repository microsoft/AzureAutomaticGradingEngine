using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class NotificationFunction
    {
        [FunctionName("NotificationFunction")]
        public static void Run([QueueTrigger("messages", Connection = "")]string queueItem, ILogger log)
        {
            log.LogInformation($"C# Queue trigger function processed: {queueItem}");


        }
    }
}
