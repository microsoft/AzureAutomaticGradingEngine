using System;
using Azure;
using Azure.Data.Tables;

namespace AzureAutomaticGradingEngineFunctionApp.Model
{


    class Assignment : ITableEntity
    {
        public string GraderUrl { get; set; }
        public string TeacherEmail { get; set; }
        public bool? SendMarkEmailToStudents { get; set; }
        public string CronExpression { get; set; }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

 
}
