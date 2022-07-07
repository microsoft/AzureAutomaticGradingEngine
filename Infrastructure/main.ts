import { Construct } from "constructs";
import { App, TerraformOutput, TerraformStack } from "cdktf";
import { AzurermProvider, ResourceGroup, StorageAccount, StorageQueue, StorageTable, StorageContainer } from "cdktf-azure-providers/.gen/providers/azurerm";
import { StringResource } from 'cdktf-azure-providers/.gen/providers/random'
import { AzureFunctionLinuxConstruct, PublishMode } from "azure-common-construct/patterns/AzureFunctionLinuxConstruct";
import path = require("path");

class AzureAutomaticGradingEngineStack extends TerraformStack {
  constructor(scope: Construct, name: string) {
    super(scope, name);

    new AzurermProvider(this, "AzureRm", {
      features: {}
    })

    const prefix = "GradingEngine"
    const environment = "dev"

    const resourceGroup = new ResourceGroup(this, "ResourceGroup", {
      location: "EastAsia",
      name: prefix + "ResourceGroup"
    })

    const suffix = new StringResource(this, "Random", {
      length: 5,
      special: false,
      lower: true,
      upper: false,
    })
    const storageAccount = new StorageAccount(this, "StorageAccount", {
      name: prefix.toLocaleLowerCase() + environment.toLocaleLowerCase() + suffix.result,
      location: resourceGroup.location,
      resourceGroupName: resourceGroup.name,
      accountTier: "Standard",
      accountReplicationType: "LRS"
    })

    const tables = ["Assignment", "LabCredential", "Subscription"];
    tables.map(t => new StorageTable(this, t + "StorageTable", {
      name: t,
      storageAccountName: storageAccount.name
    }))

    const queues = ["start-event", "end-event"];
    queues.map(q =>
      new StorageQueue(this, q + "StorageQueue", {
        name: q,
        storageAccountName: storageAccount.name
      })
    )
    const blogStorages = ["report", "testresult"]
    blogStorages.map(b =>
      new StorageContainer(this, b + "StorageContainer", {
        name: b,
        storageAccountName: storageAccount.name
      })
    )

    const appSettings = {  
      "CalendarUrl": process.env.CALENDAR_URL!,
      "EmailSmtp": process.env.EMAIL_SMTP!,
      "CommunicationServiceConnectionString": process.env.COMMUNICATION_SERVICE_CONNECTION_STRING!,
      "EmailUserName": process.env.EMAIL_USERNAME!,
      "EmailPassword": process.env.EMAIL_PASSWORD!,
      "EmailFromAddress": process.env.EMAIL_FROM_ADDRESS!,
      "StorageAccountConnectionString": storageAccount.primaryConnectionString
    }

    const azureFunctionConstruct = new AzureFunctionLinuxConstruct(this, "AzureFunctionConstruct", {
      functionAppName: process.env.FUNCTION_APP_NAME!,
      environment,
      prefix,
      resourceGroup,
      appSettings,
      vsProjectPath: path.join(__dirname, "..", "AzureAutomaticGradingEngineFunctionApp/"),
      publishMode: PublishMode.AfterCodeChange
    })
    new TerraformOutput(this, "FunctionAppHostname", {
      value: azureFunctionConstruct.functionApp.name
    })
    new TerraformOutput(this, "AzureFunctionBaseUrl", {
      value: `https://${azureFunctionConstruct.functionApp.name}.azurewebsites.net`
    })
    new TerraformOutput(this, "StudentRegistrationFunctionUrl", {
      value: `https://${azureFunctionConstruct.functionApp.name}.azurewebsites.net/api/StudentRegistrationFunction?email=xxx@abc.com&lab=examplelab`
    })
  }
}

const app = new App({ skipValidation: true });
new AzureAutomaticGradingEngineStack(app, "AzureAutomaticGradingEngine");
app.synth();
