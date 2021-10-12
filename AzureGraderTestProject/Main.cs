using System;
using System.IO;
using NUnit.Common;
using NUnitLite;

namespace AzureGraderTestProject
{
    class Run
    {
        static int Main(string[] args)
        {
            var tempCredentialsFilePath = args[0];
            var tempDir = args[1];
            var trace = args[2];
            Console.WriteLine("tempCredentialsFilePath:" + tempCredentialsFilePath);
            Console.WriteLine("trace" + trace);

            StringWriter strWriter = new StringWriter();
            var autoRun = new AutoRun();
            var returnCode = autoRun.Execute(new string[]{
                "/test:AzureGraderTestProject",
                "--work=" + tempDir,
                "--output=" + tempDir,
                "--err=" + tempDir,
                "--params:AzureCredentialsPath=" + tempCredentialsFilePath + ";trace=" + trace
            }, new ExtendedTextWrapper(strWriter), Console.In);

            var xml = File.ReadAllText(Path.Combine(tempDir, "TestResult.xml"));
            Console.WriteLine(returnCode);
            Console.WriteLine(xml);
            return 0;
        }
        private static string GetTemporaryDirectory(string trace)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), Math.Abs(trace.GetHashCode()).ToString());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
    }
}
