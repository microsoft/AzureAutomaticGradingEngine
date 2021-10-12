using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using NUnit.Framework;

namespace AzureGraderTestProject
{
    class Config
    {
        public Config()
        {
            var azureAuthFilePath = TestContext.Parameters.Get("AzureCredentialsPath", null);
            var trace = TestContext.Parameters.Get("trace", null);
            TestContext.Out.WriteLine(trace);
            Credentials = SdkContext.AzureCredentialsFactory.FromFile(azureAuthFilePath);
            SubscriptionId = Credentials.DefaultSubscriptionId;
        }
        public AzureCredentials Credentials { get; private set; }
        public string SubscriptionId { get; private set; }
    }
}