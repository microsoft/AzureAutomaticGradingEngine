using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace AzureAutomaticGradingEngineFunctionApp.Helper
{
    class CloudStorage
    {
        public static CloudStorageAccount GetCloudStorageAccount(ExecutionContext executionContext)
        {
            var config = new ConfigurationBuilder()
                            .SetBasePath(executionContext.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables().Build();
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["storageTestResult"]);
            return storageAccount;
        }
        public static async Task<List<IListBlobItem>> ListBlobsFlatListing(CloudBlobContainer cloudBlobContainer, string prefix, ILogger log, bool isToday)
        {
            var blobItems = new List<IListBlobItem>();
            BlobContinuationToken blobContinuationToken = null;

            var now = DateTime.Now;
            var nowPath = $@"/{now.Year}/{now.Month}/{now.Day}/";
            try
            {
                do
                {
                    var resultSegment = await cloudBlobContainer.ListBlobsSegmentedAsync(
                        prefix: prefix,
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.None,
                        maxResults: null,
                        currentToken: blobContinuationToken,
                        options: null,
                        operationContext: null
                    );

                    // Get the value of the continuation token returned by the listing call.
                    blobContinuationToken = resultSegment.ContinuationToken;
                    foreach (IListBlobItem item in resultSegment.Results)
                    {
                        if (isToday && item.Uri.ToString().Contains(nowPath))
                        {
                            blobItems.Add(item);
                        }
                        else
                        {
                            blobItems.Add(item);
                        }
                    }
                } while (blobContinuationToken != null);
            }
            catch (RequestFailedException e)
            {
                log.LogInformation(e.Message);

            }
            return blobItems;
        }

        public static async Task SaveTestResult(CloudBlobContainer container, string assignment, string email, string xml, DateTime gradeTime)
        {
            var filename = Regex.Replace(email, @"[^0-9a-zA-Z]+", "");
            var blobName = string.Format(CultureInfo.InvariantCulture, assignment + "/" + email + "/{0:yyyy/MM/dd/HH/mm}/" + filename + ".xml", gradeTime);
            Console.WriteLine(blobName);

            var blob = container.GetBlockBlobReference(blobName);
            blob.Properties.ContentType = "application/content";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(xml);
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        public static async Task SaveJsonReport(ExecutionContext executionContext, string blobName, Dictionary<string, MarkDetails> calculateMarks)
        {
            var blob = GetCloudBlockBlobInReportContainer(executionContext, blobName);
            blob.Properties.ContentType = "application/json";
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            await writer.WriteAsync(JsonConvert.SerializeObject(calculateMarks));
            await writer.FlushAsync();
            ms.Position = 0;
            await blob.UploadFromStreamAsync(ms);
        }

        public static async Task SaveExcelReport(ExecutionContext executionContext, string blobName, Stream excelMemoryStream)
        {
            var blob = GetCloudBlockBlobInReportContainer(executionContext, blobName);
            blob.Properties.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            excelMemoryStream.Position = 0;
            await blob.UploadFromStreamAsync(excelMemoryStream);
        }

        private static CloudBlockBlob GetCloudBlockBlobInReportContainer(ExecutionContext executionContext, string blobName)
        {
            var container = GetCloudBlobContainer(executionContext, "report");
            var blob = container.GetBlockBlobReference(blobName);
            return blob;
        }

        public static CloudBlobContainer GetCloudBlobContainer(ExecutionContext executionContext, string containerName)
        {
            var storageAccount = CloudStorage.GetCloudStorageAccount(executionContext);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerName);
            return container;
        }
    }
}
