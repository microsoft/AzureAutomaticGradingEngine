using System.Runtime.Serialization;

namespace AzureAutomaticGradingEngineFunctionApp.Model
{
    [DataContract]
    public class Confidential
    {
        [DataMember] public string appId;
        [DataMember] public string displayName;
        [DataMember] public string tenant;
        [DataMember] public string password;    
    }

    public class Student
    {
        [DataMember] public string email;
        [DataMember] public Confidential credentials;     
    }

}