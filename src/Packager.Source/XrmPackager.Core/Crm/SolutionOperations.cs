namespace XrmPackager.Core.Crm;

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

public sealed class SolutionOperations
{
    private readonly ILogger _logger;

    public SolutionOperations(ILogger logger)
    {
        _logger = logger;
    }

    public void TestConnection(ServiceClient client)
    {
        if (!client.IsReady)
        {
            throw new CrmOperationException(client.LastError ?? "Dataverse client is not ready.");
        }

        _logger.Info("Connection test successful.");
        _logger.Info($"Organization: {client.ConnectedOrgFriendlyName}");
        _logger.Info($"Organization ID: {client.ConnectedOrgId}");
        _logger.Info($"Version: {client.ConnectedOrgVersion}");
    }

    public void ImportSolution(ServiceClient client, SolutionImportOptions options)
    {
        if (!File.Exists(options.ZipPath))
        {
            throw new InvalidArgumentException($"Solution zip was not found: {options.ZipPath}");
        }

        try
        {
            _logger.Info($"Importing solution from: {options.ZipPath}");
            var solutionBytes = File.ReadAllBytes(options.ZipPath);

            var importRequest = new ImportSolutionRequest
            {
                CustomizationFile = solutionBytes,
                PublishWorkflows = options.PublishAfterImport,
                OverwriteUnmanagedCustomizations = options.OverwriteUnmanagedCustomizations,
                SkipProductUpdateDependencies = options.SkipProductUpdateDependencies,
                ConvertToManaged = options.ConvertToManaged,
            };

            client.Execute(importRequest);
            _logger.Info("Solution import completed.");

            if (options.PublishAfterImport)
            {
                PublishAll(client);
            }
        }
        catch (Exception ex)
        {
            throw new CrmOperationException($"Failed to import solution: {ex.Message}", ex);
        }
    }

    public void PublishAll(ServiceClient client)
    {
        try
        {
            _logger.Info("Publishing all customizations...");
            client.Execute(new PublishAllXmlRequest());
            _logger.Info("Publish completed.");
        }
        catch (Exception ex)
        {
            throw new CrmOperationException($"Failed to publish customizations: {ex.Message}", ex);
        }
    }
}
