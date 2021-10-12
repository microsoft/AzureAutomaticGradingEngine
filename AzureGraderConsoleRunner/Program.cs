using NUnit.Engine;
using System;
using System.IO;
using System.Xml;


namespace AzureGraderConsoleRunner
{
    class Program
    {

        static void Main(string[] args)
        {
            string path = Directory.GetCurrentDirectory();
            Console.WriteLine(path);
            var pathToTestLibrary = path.Replace("AzureGraderConsoleRunner", "AzureGraderTestProject") + "\\AzureGraderTestProject.dll";
            var azureAuthFilePath = @"C:\Users\developer\Documents\azureauth.json";

            Program runner = new Program();
            Console.WriteLine("Run Test Now.");
            runner.Run(pathToTestLibrary, azureAuthFilePath);
            Console.ReadLine();
        }

        public void Run(string pathToTestLibrary, string azureAuthFilePath)
        {
            ITestEngine engine = TestEngineActivator.CreateInstance();
            // Create a simple test package - one assembly, no special settings
            TestPackage package = new TestPackage(pathToTestLibrary);
            package.Settings.Add("AzureCredentialsPath", azureAuthFilePath);
            // Get a runner for the test package
            ITestRunner runner = engine.GetRunner(package);
            // Run all the tests in the assembly
            XmlNode testResult = runner.Run(listener: null, TestFilter.Empty);
            testResult!.OwnerDocument!.Save("test_result.xml");
            var xml = File.ReadAllText("test_result.xml");
            Console.WriteLine(xml);

        }
    }
}
