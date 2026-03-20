namespace XrmPackager.Commands;

using Microsoft.PowerPlatform.Dataverse.Client;
using XrmPackager.Core;

internal static class CommandExecution
{
    internal static int ExecuteWithClient(ILogger logger, Action<ServiceClient> operation, bool logStartupDetails = false)
    {
        using var client = CommandDataverseClientFactory.Create(logger, logStartupDetails);
        operation(client);
        return 0;
    }
}
