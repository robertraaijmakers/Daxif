namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Crm;

public sealed class PluginSyncCommand
{
    private readonly ILogger _logger;

    public PluginSyncCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "sync", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidArgumentException("Plugin command requires: plugin sync");
        }

        var assemblyPaths = new List<string>();
        var solutionName = string.Empty;
        var isolationMode = AssemblyIsolationMode.Sandbox;
        var dryRun = false;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly":
                case "-a":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--assembly requires a value.");
                    }
                    assemblyPaths.Add(args[++i]);
                    break;
                case "--solution":
                case "-s":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException("--solution requires a value.");
                    }
                    solutionName = args[++i];
                    break;
                case "--isolation-mode":
                    if (i + 1 >= args.Length)
                    {
                        throw new InvalidArgumentException(
                            "--isolation-mode requires a value: sandbox|none."
                        );
                    }
                    isolationMode = ParseIsolationMode(args[++i]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new InvalidArgumentException($"Unknown plugin option: {args[i]}");
            }
        }

        if (assemblyPaths.Count == 0)
        {
            throw new InvalidArgumentException(
                "At least one --assembly is required for plugin sync."
            );
        }

        if (string.IsNullOrWhiteSpace(solutionName))
        {
            throw new InvalidArgumentException("--solution is required for plugin sync.");
        }

        var invalidAssemblyPath = assemblyPaths.FirstOrDefault(path => !File.Exists(path));
        if (invalidAssemblyPath != null)
        {
            throw new InvalidArgumentException($"Plugin assembly not found: {invalidAssemblyPath}");
        }

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var operations = new PluginOperations(_logger);

                foreach (var assemblyPath in assemblyPaths)
                {
                    _logger.Info($"Synchronizing assembly: {assemblyPath}");
                    operations.SyncPluginAssembly(
                        client,
                        new PluginSyncOptions
                        {
                            AssemblyPath = assemblyPath,
                            SolutionName = solutionName,
                            IsolationMode = isolationMode,
                            DryRun = dryRun,
                        }
                    );
                }
            }
        );
    }

    private static AssemblyIsolationMode ParseIsolationMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "sandbox" => AssemblyIsolationMode.Sandbox,
            "none" => AssemblyIsolationMode.None,
            _ => throw new InvalidArgumentException(
                "--isolation-mode must be 'sandbox' or 'none'."
            ),
        };
    }
}
