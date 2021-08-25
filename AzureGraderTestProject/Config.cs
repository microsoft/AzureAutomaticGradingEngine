using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;

namespace AzureGraderTestProject
{
    class Config
    {
        public Config()
        {
            var azureAuthFilePath = Environment.GetEnvironmentVariable("AzureAuthFilePath") ?? @"C:\Users\developer\Documents\azureauth.json";
            Credentials = SdkContext.AzureCredentialsFactory.FromFile(azureAuthFilePath);
            SubscriptionId = Credentials.DefaultSubscriptionId; 
        }
        public AzureCredentials Credentials { get; private set; }
        public string SubscriptionId { get; private set; }
    }
}
