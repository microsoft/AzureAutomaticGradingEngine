using System;
using System.Collections.Generic;
using System.Text;

namespace AzureAutomaticGradingEngineFunctionApp.Model
{
    public class Assignment
    {
        public string Name { get; set; }
        public string TeacherEmail { get; set; }
        public bool SendMarkEmailToStudents { get; set; }
        public ClassContext Context { get; set; }
        
    }

    public class ClassContext
    {
        public string GraderUrl { get; set; }
        public string Students { get; set; }
    }

    public class EmailMessage
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    public class ClassGradingJob
    {
        public Assignment assignment { get; set; }
        public string graderUrl { get; set; }
        public dynamic students { get; set; }
    }

    public class SingleGradingJob
    {
        public Assignment assignment { get; set; }
        public string graderUrl { get; set; }
        public dynamic student { get; set; }
    }

}
