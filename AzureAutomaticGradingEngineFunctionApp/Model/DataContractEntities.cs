using System.Runtime.Serialization;

namespace AzureAutomaticGradingEngineFunctionApp.Model
{
    [DataContract]
    public class Confidential
    {
        [DataMember] public string clientId;
        [DataMember] public string clientSecret;
        [DataMember] public string subscriptionId;
        [DataMember] public string tenantId;
        [DataMember] public string activeDirectoryEndpointUrl;
        [DataMember] public string resourceManagerEndpointUrl;
        [DataMember] public string activeDirectoryGraphResourceId;
        [DataMember] public string sqlManagementEndpointUrl;
        [DataMember] public string galleryEndpointUrl;
        [DataMember] public string managementEndpointUrl;
    }
   
}