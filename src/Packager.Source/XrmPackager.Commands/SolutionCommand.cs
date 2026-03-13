namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Crm;

public sealed class SolutionCommand
{
    private readonly ILogger _logger;

    public SolutionCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidArgumentException(
                "Solution command requires a subcommand: import or publish."
            );
        }

        var subcommand = args[1].ToLowerInvariant();
        return subcommand switch
        {
            "import" => ExecuteImport(args),
            "publish" => ExecutePublish(),
            _ => throw new InvalidArgumentException($"Unknown solution subcommand: {subcommand}"),
        };
    }

    private int ExecuteImport(string[] args)
    {
        var zipPath = string.Empty;
        var publish = false;
        var overwrite = false;
        var skipDependencies = false;
        var convertToManaged = false;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--zip":
                case "-z":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--zip requires a value.");
                    }
                    zipPath = args[++i];
                    break;
                case "--publish":
                case "-p":
                    publish = true;
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--skip-dependencies":
                    skipDependencies = true;
                    break;
                case "--convert-to-managed":
                    convertToManaged = true;
                    break;
                default:
                    throw new InvalidArgumentException($"Unknown import option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new InvalidArgumentException("--zip is required for solution import.");
        }

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var operations = new SolutionOperations(_logger);
                operations.ImportSolution(
                    client,
                    new SolutionImportOptions
                    {
                        ZipPath = zipPath,
                        PublishAfterImport = publish,
                        OverwriteUnmanagedCustomizations = overwrite,
                        SkipProductUpdateDependencies = skipDependencies,
                        ConvertToManaged = convertToManaged,
                    }
                );
            }
        );
    }

    private int ExecutePublish()
    {
        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var operations = new SolutionOperations(_logger);
                operations.PublishAll(client);
            }
        );
    }
}
