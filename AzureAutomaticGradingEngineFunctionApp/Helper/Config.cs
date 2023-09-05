using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;

namespace AzureAutomaticGradingEngineFunctionApp.Helper
{
    public class Config
    {
        private readonly ExecutionContext _context;

        public Config(ExecutionContext context)
        {
            this._context = context;
        }

        public enum Key
        {
            StorageAccountConnectionString,
            EmailSmtp,
            EmailUserName,
            EmailPassword,
            EmailFromAddress,
            Environment
        };

        public string GetConfig(Key key)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(_context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var name = Enum.GetName(typeof(Key), key);
            return config[name];
        }
    }
}