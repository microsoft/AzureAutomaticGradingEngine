using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Collections.Generic;
using AzureAutomaticGradingEngineFunctionApp;

namespace AzureGraderFunctionApp
{
    public static class StudentRegistrationFunction
    {

        class Confidential
        {
            public string clientId;
            public string clientSecret;
            public string subscriptionId;
            public string tenantId;
            public string activeDirectoryEndpointUrl;
            public string resourceManagerEndpointUrl;
            public string activeDirectoryGraphResourceId;
            public string sqlManagementEndpointUrl;
            public string galleryEndpointUrl;
            public string managementEndpointUrl;
        }

        [FunctionName("StudentRegistrationFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext context)
        {
            log.LogInformation("Start AzureGraderFunction");


            if (req.Method == "GET")
            {

                if (!req.Query.ContainsKey("email") || !req.Query.ContainsKey("project"))
                {
                    return new ContentResult()
                    {
                        Content = "Invalid Url and it should contain project and email!",
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }
                else
                {
                    string project = req.Query["project"];
                    string email = req.Query["email"];
                    string html = $@"
<!DOCTYPE html>

<html lang='en' xmlns='http://www.w3.org/1999/xhtml'>
<head>
    <meta charset='utf-8' />
    <title>Azure Grader</title>
</head>
<body>
    <form id='form' method='post'>
        <input type='hidden' id='classroomName' name='project' value='{project}'>
        <label for='Email'>Email:</label><br>
        <input type='email' id='email' name='email' size='50' value='{email}' required><br>
        Azure Credentials<br/>
        <textarea name='credentials' required  rows='15' cols='100'></textarea>
        <br/>
        <button type='submit'>Register</button>
    </form>
    <footer>
        <p>Developed by <a href='https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/'> Higher Diploma in Cloud and Data Centre Administration Team.</a></p>
    </footer>
</body>
</html>";

                    return new ContentResult()
                    {
                        Content = html,
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }
            }
            else if (req.Method == "POST")
            {
                log.LogInformation("POST Request");

                log.LogInformation("Form Submit");
                string project = req.Form["project"];
                string email = req.Form["email"];
                string credentials = req.Form["credentials"];
                log.LogInformation("Student Register: " + email + " Project:" + project);
                if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(credentials))
                {
                    return new ContentResult
                    {
                        Content = "Missing Data and Registration Failed",
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }

                var config = new ConfigurationBuilder()
                                .SetBasePath(context.FunctionAppDirectory)
                                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .Build();
                var connectionString = config["storageTestResult"];

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tblclient = storageAccount.CreateCloudTableClient();
                CloudTable credentialsTable = tblclient.GetTableReference("credentials");
                CloudTable subscriptionTable = tblclient.GetTableReference("subscriptions");

                var credential = Newtonsoft.Json.JsonConvert.DeserializeObject<Confidential>(credentials);
                string subscriptionId = credential.subscriptionId;

                TableResult result = await subscriptionTable.ExecuteAsync(TableOperation.Retrieve(project, subscriptionId, new List<string>() { "Email" }));

                Console.WriteLine(result.Result);
                if (result.Result != null)
                {
                    return new ContentResult
                    {
                        Content = "Duplicated Subscription Id",
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }
                var subscription = new Subscription()
                {
                    PartitionKey = project,
                    RowKey = subscriptionId,
                    Email = email
                };
                await subscriptionTable.ExecuteAsync(TableOperation.Insert(subscription));

                var projectCredential = new ProjectCredential()
                {
                    PartitionKey = project,
                    RowKey = email,
                    Timestamp = DateTime.Now,
                    Credentials = credentials
                };
                await credentialsTable.ExecuteAsync(TableOperation.InsertOrReplace(projectCredential));


                return new ContentResult
                {
                    Content = "Registered",
                    ContentType = "text/html",
                    StatusCode = 200,
                };
            }

            return new OkObjectResult("ok");
        }



    }




}
