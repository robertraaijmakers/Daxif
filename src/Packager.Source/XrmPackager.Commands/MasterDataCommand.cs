namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Crm;

public sealed class MasterDataCommand
{
    private readonly ILogger _logger;

    public MasterDataCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidArgumentException(
                "masterdata command requires a subcommand: export or import"
            );
        }

        return args[1].ToLowerInvariant() switch
        {
            "export" => ExecuteExport(args),
            "import" => ExecuteImport(args),
            _ => throw new InvalidArgumentException(
                $"Unknown masterdata subcommand '{args[1]}'. Valid subcommands: export, import"
            ),
        };
    }

    private int ExecuteExport(string[] args)
    {
        var schemaPath = string.Empty;
        var dataFolder = string.Empty;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--schema":
                case "-s":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--schema requires a value.");
                    }
                    schemaPath = args[++i];
                    break;
                case "--folder":
                case "-f":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--folder requires a value.");
                    }
                    dataFolder = args[++i];
                    break;
                default:
                    throw new InvalidArgumentException(
                        $"Unknown masterdata export option: {args[i]}"
                    );
            }
        }

        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            throw new InvalidArgumentException("--schema is required for masterdata export.");
        }

        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new InvalidArgumentException("--folder is required for masterdata export.");
        }

        var exportOptions = new MasterDataExportOptions
        {
            SchemaPath = schemaPath,
            DataFolder = dataFolder,
        };

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var operations = new MasterDataOperations(_logger);
                if (schemaPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    operations.ExportJson(client, exportOptions);
                else
                    operations.Export(client, exportOptions);
            }
        );
    }

    private int ExecuteImport(string[] args)
    {
        var schemaPath = string.Empty;
        var dataFolder = string.Empty;
        var dryRun = false;
        var logChanges = false;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--schema":
                case "-s":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--schema requires a value.");
                    }
                    schemaPath = args[++i];
                    break;
                case "--folder":
                case "-f":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--folder requires a value.");
                    }
                    dataFolder = args[++i];
                    break;
                case "--dry-run":
                case "-d":
                    dryRun = true;
                    break;
                case "--log-changes":
                case "-l":
                    logChanges = true;
                    break;
                default:
                    throw new InvalidArgumentException(
                        $"Unknown masterdata import option: {args[i]}"
                    );
            }
        }

        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            throw new InvalidArgumentException("--schema is required for masterdata import.");
        }

        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new InvalidArgumentException("--folder is required for masterdata import.");
        }

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var importOptions = new MasterDataImportOptions
                {
                    SchemaPath = schemaPath,
                    DataFolder = dataFolder,
                    DryRun = dryRun,
                    LogChanges = logChanges,
                };
                var operations = new MasterDataOperations(_logger);
                if (schemaPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    operations.ImportJson(client, importOptions);
                else
                    operations.Import(client, importOptions);
            }
        );
    }
}
