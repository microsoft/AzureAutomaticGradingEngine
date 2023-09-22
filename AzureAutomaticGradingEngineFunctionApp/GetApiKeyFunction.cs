using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AzureAutomaticGradingEngineFunctionApp.Dao;
using AzureAutomaticGradingEngineFunctionApp.Helper;

namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class GetApiKeyFunction
    {
        [FunctionName(nameof(GetApiKeyFunction))]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ExecutionContext context,
            ILogger log)
        {
            log.LogInformation("GetApiKeyFunction HTTP trigger function processed a request.");

            var config = new Config(context);
            var dao = new LabCredentialDao(config, log);
            string course = req.Query["course"];
            string email = req.Query["email"];
            var credential = dao.Get(course, email);
            var json = JsonConvert.SerializeObject(credential);
            return new JsonResult(credential);

        }
    }
}
