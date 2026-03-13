namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Crm;

public sealed class TestConnectionCommand
{
    private readonly ILogger _logger;

    public TestConnectionCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute()
    {
        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                _logger.Info("Running Dataverse test query...");
                var operations = new SolutionOperations(_logger);
                operations.TestConnection(client);
                _logger.Info("Dataverse connection test completed.");
            },
            logStartupDetails: true
        );
    }
}
