using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace AzureAutomaticGradingEngineFunctionApp
{
    class Assignment : TableEntity
    {
        public string GraderUrl { get; set; }
    }
    class ProjectCredential : TableEntity
    {
        public string Credentials { get; set; }
    }
    class Subscription : TableEntity
    {
        public string Email { get; set; }
    }
}
