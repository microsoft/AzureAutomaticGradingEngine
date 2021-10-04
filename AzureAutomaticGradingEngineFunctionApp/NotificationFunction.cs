using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class NotificationFunction
    {
        [FunctionName("NotificationFunction")]
        [StorageAccount("storageTestResult")]
        public static void Run([QueueTrigger("messages")] EmailMessage queueItem, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"NotificationFunction Queue trigger function processed: {queueItem}");

            var config = new Config(context);
            var email = new Email(config, log);

            email.Send(queueItem);
        }
    }
}
