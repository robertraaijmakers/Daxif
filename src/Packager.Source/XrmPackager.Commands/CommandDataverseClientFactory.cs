namespace XrmPackager.Commands;

using Microsoft.PowerPlatform.Dataverse.Client;
using XrmPackager.Core;
using XrmPackager.Core.Authentication;

internal static class CommandDataverseClientFactory
{
    internal static ServiceClient Create(ILogger logger, bool logStartupDetails = false)
    {
        if (logStartupDetails)
        {
            logger.Info("Loading Dataverse authentication configuration from environment...");
        }

        var authConfig = AuthenticationConfigLoader.LoadFromEnvironment();

        if (logStartupDetails)
        {
            logger.Info(
                $"Configuration loaded. URL: {authConfig.OrganizationUrl}, AuthType: {authConfig.AuthMethod}"
            );
            logger.Info("Initializing Dataverse client factory...");
        }

        var clientFactory = new ServiceClientFactory(authConfig, logger);

        if (logStartupDetails)
        {
            logger.Info("Starting Dataverse sign-in flow...");
        }

        return clientFactory.CreateServiceClient();
    }
}
