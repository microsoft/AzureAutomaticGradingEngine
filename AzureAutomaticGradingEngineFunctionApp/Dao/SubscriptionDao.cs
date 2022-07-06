using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp.Dao;

internal class SubscriptionDao : Dao<Subscription>
{
    public SubscriptionDao(Config config, ILogger logger) : base(config, logger)
    {
    }
}