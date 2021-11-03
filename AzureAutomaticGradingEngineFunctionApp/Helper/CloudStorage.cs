using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

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
    }
}
