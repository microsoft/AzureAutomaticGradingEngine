# Azure Automatic Grading Engine

For any course testing Microsoft Azure, it is hard to assess or grade Azure projects manually. This project makes use of the technique of unit tests to grade students' Azure project settings automatically.

This project has been developed by [Cyrus Wong]( https://www.linkedin.com/in/cyruswong) [Microsoft Learn Educator Ambassador](https://docs.microsoft.com/learn/roles/educator/learn-for-educators-overview/?WT.mc_id=academic-39456-leestott) in Association with the [Microsoft Next Generation Developer Relations Team](https://techcommunity.microsoft.com/t5/educator-developer-blog/bg-p/EducatorDeveloperBlog?WT.mc_id=academic-39457-leestott).
Project collaborators include [Chan Yiu Leung](https://www.linkedin.com/in/hadeschan/), [So Ka Chun](https://www.linkedin.com/in/so-ka-chun-0643971a5/), [Lo Chun Hei](https://www.linkedin.com/in/chunhei-lo-86a9301b5/), [Ling Po Chu](https://www.linkedin.com/in/po-chu-ling-88392b1b5/), [Cheung Ho Shing](https://www.linkedin.com/in/cheunghoshing/) and [Pearly Law](https://www.linkedin.com/in/mei-ching-pearly-jean-law-172707171/) from the IT114115 Higher Diploma in Cloud and Data Centre Administration.

The project is being validated through usage on the course [Higher Diploma in Cloud and Data Centre Administration](https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/)


## Microsoft Reactor - Getting started with Microsoft Azure Automatic Grading Engine
[![Getting started with Microsoft Azure Automatic Grading Engine](http://img.youtube.com/vi/5jnVk8o8G7k/0.jpg)](https://youtu.be/5jnVk8o8G7k "Getting started with Microsoft Azure Automatic Grading Engine")


### Architecture

![Architecture](./images/GraderArchitecture.png)

You can read more about this project at [Microsoft Educator Developer TechCommunity](https://techcommunity.microsoft.com/t5/educator-developer-blog/microsoft-azure-automatic-grading-engine-oct-2021-update/ba-p/2849141?WT.mc_id=academic-39457-leestott)

## CDK-TF Deployment 
You have to refer [Object Oriented Your Azure Infrastructure with Cloud Development Kit for Terraform (CDKTF)](https://techcommunity.microsoft.com/t5/educator-developer-blog/object-oriented-your-azure-infrastructure-with-cloud-development/ba-p/3474715) and setup your CDK-TF.
```
npm i
cdktf deploy --auto-approve
```

## Config SMTP
You have to set the App Settings Key during deployment or in the Azure Portal. If you want to use Gmail, you need to allow [Less Secure Apps](https://myaccount.google.com/lesssecureapps) for your Gmail.


## Deploy the Demo Assignment Project
Please visit [Azure Automatic Grading Engine - Classroom Assignments samples](https://github.com/microsoft/AzureAutomaticGradingEngine_Assignments)


## Supporting Environment

This service is tested with [Azure for student subscription](http://aka.ms/azure4students) and follows details relating to the use of the [Azure SDK](https://devblogs.microsoft.com/azure-sdk/authentication-and-the-azure-sdk?WT.mc_id=academic-39456-leestott)

# How to Define a Project Assignment?

## Define a project or assignment

You need to add a Entity in assignments table.
Partition Key: assignment or project name such as it114115
Properties:
1. "GraderUrl":  The grader HTTP Url 
2. "CronExpression" : The grading schedule. The time must be must be divided by 5 minutes. [Follow Cronos Cron expression sample](https://github.com/HangfireIO/Cronos) use the expression */5 * * * *	Every 5 minutes
3. "TeacherEmail":  Teacher Email for class grade report. (Optional)
4. "SendMarkEmailToStudents": This is a type boolean, set it to true if you want to send the mark report to students via an email. If this email property does not exist or the item is set to false, the email will not be sent.

![Assignment](./images/AssignmentTableRecord.png)

# Email the Registration Link to your Student

[![How to mail merge registration information to your students](http://img.youtube.com/vi/CXc7fx6nNJk/0.jpg)](https://youtu.be/CXc7fx6nNJk "How to mail merge registration information to your students?")

To prevent typos of the assignment name and email address, you can use mail merge to send the link to students.

You can use the sample mail merge template [/MailMerge](https://github.com/microsoft/AzureAutomaticGradingEngine/tree/main/MailMerge)

Using the template will result in your creating and issuing a unique URL string for each student. The url string will be in the following format.

https://somethingunique.azurewebsites.net/api/StudentRegistrationFunction?project=studnetid&email=studnetemailaddress

## Student Registration Steps

[![How to register your student subscription into Azure Automatic Grading Engine](http://img.youtube.com/vi/t7PEhPoilLY/0.jpg)](https://youtu.be/t7PEhPoilLY "How to register your student subscription into Azure Automatic Grading Engine")
1.	Login into your Azure Portal
2.	Check your Subscription ID
3.	Open Cloud Shell
4.	Change your subscription
<code>az account set --subscription your-subscriptions-id</code>
5.	Check the current subscriptions
<code>az account show</code>
6.	Create Subscriptions Contributor, keep it privately and don't run this command again
<code>az ad sp create-for-rbac -n "AzureAutomaticGradingEngine" --role="Contributor" --scopes="/subscriptions/your-subscriptions-id"</code>
7.	Submit the online registration form.
https://XXXXXX.azurewebsites.net/api/StudentRegistrationFunction?project=AssignmentName&email=abcd@stu.vtc.edu.hk

Note: 

1. Subscription ID and Email must be unique for each assignment.
2. Don't run <code>az ad sp create-for-rbac -n "AzureAutomaticGradingEngine" --role="Contributor" --scopes="/subscriptions/your-subscriptions-id"</code> repeatedly! Students need to submit the online form again with the new credentials.

## Generate the Prebuilt package

Get the latest zip package
AzureAutomaticGradingEngine\AzureAutomaticGradingEngineFunctionApp\obj\Release\netcoreapp3.1\PubTmp 

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
