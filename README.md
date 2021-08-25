# Azure Automatic Grading Engine

For course testing Microsoft Azure, it is hard to assess or grade Azure project manually. This project makes use of the technique of unit test to grade student's Azure project settings automatically.

This project has been developed by [Cyrus Wong]( https://www.linkedin.com/in/cyruswong) [Microsoft Learn Educator Ambassador](https://docs.microsoft.com/learn/roles/educator/learn-for-educators-overview) in Association with the [Microsoft Next Generation Developer Relations Team](https://techcommunity.microsoft.com/t5/educator-developer-blog/bg-p/EducatorDeveloperBlog?WT.mc_id=academic-39457-leestott).
Project collaborators include, [Chan Yiu Leung](https://www.linkedin.com/in/hadeschan/), [So Ka Chun](https://www.linkedin.com/in/so-ka-chun-0643971a5/), [Lo Chun Hei](https://www.linkedin.com/in/chunhei-lo-86a9301b5/), [Ling Po Chu](https://www.linkedin.com/in/po-chu-ling-88392b1b5/), [Cheung Ho Shing](https://www.linkedin.com/in/cheunghoshing/) and [Pearly Law](https://www.linkedin.com/in/mei-ching-pearly-jean-law-172707171/) from the IT114115 Higher Diploma in Cloud and Data Centre Administration.

The project is being validated through usage on the course [Higher Diploma in Cloud and Data Centre Administration](https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/)

## Deployment

### Architecture

![Architecture](./images/GraderArchitecture.png)

You can read more about this project at [Microsoft Educator Developer TechCommunity](https://techcommunity.microsoft.com/t5/educator-developer-blog/microsoft-azure-automatic-grading-engine/ba-p/2681809?WT.mc_id=academic-39457-leestott)
## Prerequisite

- 1 Storage account with 2 containers
- testresult and credentials with resource group name "azureautomaticgradingengine".

[![Deploy To Azure](https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazure.svg?sanitize=true)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2FAzureAutomaticGradingEngine%2Fmaster%2Fazuredeploy.json)

https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2FAzureAutomaticGradingEngine%2Fmaster%2Fazuredeploy.json 

Follow the following video:

[![IMAGE ALT TEXT](http://img.youtube.com/vi/LClFO3OkThY/0.jpg)](https://youtu.be/LClFO3OkThY "How to deploy Azure Automatic Grading Engine")

## Installation Steps

1. Clone this repo.
2. Set Publish configuration.
3. Create assignment json.

## Deploy Demo Assignment Project

This is a sample example where we can validate if students have successfully deployed a Azure VNET within a Azure Resource Group called IT114115.

As an educator you will need to provide fixed names for resources which you expect students to create within their Azure subscriptions.

In this example we expect students to create a simple VNET ARM and a resouce group named IT114115
https://docs.microsoft.com/azure/virtual-network/quick-create-template

For the test details,
https://github.com/microsoft/AzureAutomaticGradingEngine/blob/master/AzureAutomaticGradingEngineFunctionApp/AzureGraderTest/VnetUnitTest.cs

## Supporting Environment

This service is tested with [Azure for student subscription](http://aka.ms/azure4students) and follows details in relating to the use of the [Azure SDK](https://devblogs.microsoft.com/azure-sdk/authentication-and-the-azure-sdk/)

## Student Tasks

Student will need to create a Service principal which utilises RBAC to allow the grader to inspect their Azure Subscriptions. 

Open Azure Cloud Shell and run

az ad sp create-for-rbac -n "foraazuregrader" --sdk-auth

Save down the result json into file.

Share the json file to teacher.

## Quick test with AzureGraderConsoleRunner

Open \AzureGraderTestProject\AzureGraderConsoleRunner\Program.cs and change.

Environment.SetEnvironmentVariable("AzureAuthFilePath", @"C:\Users\developer\Documents\azureauth.json");

Build and run AzureGraderConsoleRunner.

Test result will be saved in \AzureGraderTestProject\AzureGraderConsoleRunner\bin\Debug\test_result.xml

## Run with Visual Studio Test Explorer

Set up 2 system wide environment variables

set AzureAuthFilePath=C:\Users\developer\Documents\azureauth.json

Or update \repos\AzureGraderTestProject\AzureGraderTestProject\Config.cs

## Scheduler Grader

The scheduler is set to runs every 5 minutes by default and you can change the TimerTrigger expression.
https://github.com/microsoft/AzureAutomaticGradingEngine/blob/master/AzureAutomaticGradingEngineFunctionApp/ScheduleGraderFunction.cs 

testresult: saves Nunit xml test result.
credentials: define assignment and class.

For example, upload vnet.json into credentials.

```json
{
  "graderUrl": "https://xxxx.azurewebsites.net/api/AzureGraderFunction",
  "students": [
    {
      "email": "xxx@.edu",
      "credentials": {
        "activeDirectoryEndpointUrl": "https://login.microsoftonline.com",
        "activeDirectoryGraphResourceId": "https://graph.windows.net/",
        "clientId": "fjfjlfjl;afjlafjal'fjalds;f'",
        "clientSecret": "lfakl;fkdf;kal;fkalfak;",
        "galleryEndpointUrl": "https://gallery.azure.com/",
        "managementEndpointUrl": "https://management.core.windows.net/",
        "resourceManagerEndpointUrl": "https://management.azure.com/",
        "sqlManagementEndpointUrl": "https://management.core.windows.net:8443/",
        "subscriptionId": "******-******-******-********-***********",
        "tenantId": "******-******-******-********-***********"
      }
    },
    {
      "email": "yyy@.edu",
      "credentials": {
        "activeDirectoryEndpointUrl": "https://login.microsoftonline.com",
        "activeDirectoryGraphResourceId": "https://graph.windows.net/",
        "clientId": "xsxhskjdjksjdlsdjlksjdlsjd",
        "clientSecret": "gkjvkjv;ldjv'lvdvmdfhsdkfjsdl",
        "galleryEndpointUrl": "https://gallery.azure.com/",
        "managementEndpointUrl": "https://management.core.windows.net/",
        "resourceManagerEndpointUrl": "https://management.azure.com/",
        "sqlManagementEndpointUrl": "https://management.core.windows.net:8443/",
        "subscriptionId": "******-******-******-********-***********",
        "tenantId": "******-******-******-********-***********"
      }
    }
  ]
}

```

It defines assignment named "vnet" and the class with 2 students.
graderUrl is the url of Azure Function running Nunit test and return xml result.
One sample is AzureGraderFunction.cs.

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
