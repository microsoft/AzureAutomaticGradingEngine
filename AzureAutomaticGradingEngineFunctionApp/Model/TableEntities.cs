using Microsoft.WindowsAzure.Storage.Table;

namespace AzureAutomaticGradingEngineFunctionApp.Model
{
    class ProjectCredential : TableEntity
    {
        public string Credentials { get; set; }
    }
    class Subscription : TableEntity
    {
        public string Email { get; set; }
    }
}
