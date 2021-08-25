using NUnit.Engine;
using System;
using System.Xml;


namespace AzureGraderConsoleRunner
{
    class Program
    {

        static void Main(string[] args)
        {
            string pathToTestLibrary = @"C:\Users\developer\source\repos\AzureGraderTestProject\AzureGraderTestProject\bin\Debug\netcoreapp3.1\AzureGraderTestProject.dll"; //get from command line args
            Environment.SetEnvironmentVariable("AzureAuthFilePath", @"C:\Users\developer\Documents\azureauth.json");            

            Program runner = new Program();
            runner.Run(pathToTestLibrary);

            Environment.SetEnvironmentVariable("AzureAuthFilePath", null);           
        }

        public void Run(string pathToTestLibrary)
        {
            ITestEngine engine = TestEngineActivator.CreateInstance();

            // Create a simple test package - one assembly, no special settings
            TestPackage package = new TestPackage(pathToTestLibrary);

            // Get a runner for the test package
            ITestRunner runner = engine.GetRunner(package);

            // Run all the tests in the assembly
            XmlNode testResult = runner.Run(listener: null, TestFilter.Empty);
            testResult.OwnerDocument.Save("test_result.xml");
        }
    }
}
