namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Crm;

public sealed class WebResourceSyncCommand
{
    private readonly ILogger _logger;

    public WebResourceSyncCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "sync", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidArgumentException("Webresource command requires: webresource sync");
        }

        var folderPath = string.Empty;
        var solutionName = string.Empty;
        var dryRun = false;
        var publishAfterSync = true;
        var deleteMissing = true;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--folder":
                case "-f":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--folder requires a value.");
                    }
                    folderPath = args[++i];
                    break;
                case "--solution":
                case "-s":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--solution requires a value.");
                    }
                    solutionName = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-publish":
                    publishAfterSync = false;
                    break;
                case "--no-delete":
                    deleteMissing = false;
                    break;
                default:
                    throw new InvalidArgumentException($"Unknown webresource option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new InvalidArgumentException("--folder is required for webresource sync.");
        }

        if (string.IsNullOrWhiteSpace(solutionName))
        {
            throw new InvalidArgumentException("--solution is required for webresource sync.");
        }

        if (!Directory.Exists(folderPath))
        {
            throw new InvalidArgumentException($"Webresource folder not found: {folderPath}");
        }

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var operations = new WebResourceOperations(_logger);
                operations.Sync(
                    client,
                    new WebResourceSyncOptions
                    {
                        FolderPath = folderPath,
                        SolutionName = solutionName,
                        DryRun = dryRun,
                        PublishAfterSync = publishAfterSync,
                        DeleteMissing = deleteMissing,
                    }
                );
            }
        );
    }
}
