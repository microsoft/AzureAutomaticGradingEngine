using AzureAutomaticGradingEngineFunctionApp.Model;
using System;
using System.Collections.Generic;

namespace AzureAutomaticGradingEngineFunctionApp.Poco
{
    public class AssignmentPoco
    {
        public string Name { get; set; }
        public string TeacherEmail { get; set; }
        public bool SendMarkEmailToStudents { get; set; }
        public DateTime GradeTime { get; set; }
        public ClassContext Context { get; set; }

    }

    public class ClassContext
    {
        public string GraderUrl { get; set; }
        public List<Student> Students { get; set; }
    }

    public class EmailMessage
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    public class ClassGradingJob
    {
        public AssignmentPoco assignment { get; set; }
        public string graderUrl { get; set; }
        public List<Student> students { get; set; }
    }

    public class SingleGradingJob
    {
        public AssignmentPoco assignment { get; set; }
        public string graderUrl { get; set; }
        public Student student { get; set; }
    }


    public class MarkDetails
    {
        public Dictionary<string, int> Mark { get; set; }
        public Dictionary<string, DateTime> CompleteTime { get; set; }
    }

}
