using AzureAutomaticGradingEngineFunctionApp.Helper;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace AzureAutomaticGradingEngineFunctionApp.Dao;

internal class AssignmentDao : Dao<Assignment>
{
    public AssignmentDao(Config config, ILogger logger) : base(config, logger)
    {
  
    }
    public List<Assignment> GetAssignments()
    {
        var oDataQueryEntities = TableClient.Query<Assignment>();
        return oDataQueryEntities.ToList();
    }
}