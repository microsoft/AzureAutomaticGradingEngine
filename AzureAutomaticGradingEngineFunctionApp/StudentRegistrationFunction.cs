using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;


namespace AzureAutomaticGradingEngineFunctionApp
{
    public static class StudentRegistrationFunction
    {
        private static Confidential ReadToObject(string json)
        {
            var deserializedUser = new Confidential();
            var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var ser = new DataContractJsonSerializer(deserializedUser.GetType());
            deserializedUser = ser.ReadObject(ms) as Confidential;
            ms.Close();
            return deserializedUser;
        }

        [FunctionName("StudentRegistrationFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext context)
        {
            log.LogInformation("Start StudentRegistrationFunction");


            if (req.Method == "GET")
            {

                if (!req.Query.ContainsKey("email") || !req.Query.ContainsKey("project"))
                {
                    return GetContentResult("Invalid Url and it should contain project and email!");
                }
                else
                {
                    string project = req.Query["project"];
                    string email = req.Query["email"];
                    string form = $@"
    <form id='form' method='post'>
        <input type='hidden' id='classroomName' name='project' value='{project}'>
        <label for='Email'>Email:</label><br>
        <input type='email' id='email' name='email' size='50' value='{email}' required><br>
        Azure Credentials<br/>
        <textarea name='credentials' required  rows='15' cols='100'></textarea>
        <br/>
        <button type='submit'>Register</button>
    </form>
   ";
                    return GetContentResult(form);
                }
            }
            else if (req.Method == "POST")
            {
                log.LogInformation("POST Request");
                string project = req.Form["project"];
                string email = req.Form["email"];
                string credentials = req.Form["credentials"];
                log.LogInformation("Student Register: " + email + " Project:" + project);
                if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(credentials))
                {
                    return GetContentResult("Missing Data and Registration Failed!");
                }

                var config = new ConfigurationBuilder()
                                .SetBasePath(context.FunctionAppDirectory)
                                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .Build();
                var connectionString = config["storageTestResult"];

                var storageAccount = CloudStorageAccount.Parse(connectionString);
                var cloudTableClient = storageAccount.CreateCloudTableClient();
                var credentialsTable = cloudTableClient.GetTableReference("credentials");
                var subscriptionTable = cloudTableClient.GetTableReference("subscriptions");

                var credential = ReadToObject(credentials);
                var subscriptionId = credential.subscriptionId;

                var result = await subscriptionTable.ExecuteAsync(TableOperation.Retrieve<Subscription>(project, subscriptionId));

                Console.WriteLine(result.Result);
                if (result.Result != null && ((Subscription)result.Result).Email != email.ToLower().Trim())
                {
                    return GetContentResult("You can only have one Subscription Id for one assignment!");
                }
                var subscription = new Subscription()
                {
                    PartitionKey = project,
                    RowKey = subscriptionId,
                    Email = email
                };
                await subscriptionTable.ExecuteAsync(TableOperation.InsertOrReplace(subscription));

                var projectCredential = new ProjectCredential()
                {
                    PartitionKey = project,
                    RowKey = email.ToLower().Trim(),
                    Timestamp = DateTime.Now,
                    Credentials = credentials
                };
                await credentialsTable.ExecuteAsync(TableOperation.InsertOrReplace(projectCredential));

                return GetContentResult("Your credentials has been " + (result.Result != null ? "Updated!" : "Registered!"));
            }

            return new OkObjectResult("ok");
        }

        private static ContentResult GetContentResult(string content)
        {
            return new ContentResult
            {
                Content = GetHtml(content),
                ContentType = "text/html",
                StatusCode = 200,
            };
        }
        private static string GetHtml(string content)
        {
            return $@"
<!DOCTYPE html>
<html lang='en' xmlns='http://www.w3.org/1999/xhtml'>
<head>
    <meta charset='utf-8' />
    <title>Azure Grader</title>
</head>
<body>
    {content}
    <footer>
        <p>Developed by <a href='https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/'> Higher Diploma in Cloud and Data Centre Administration Team.</a></p>
    </footer>
</body>
</html>";
        }
    }
}
