namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Generation.Kotlin;

public sealed class KotlinGenerateCommand
{
    private readonly ILogger _logger;

    public KotlinGenerateCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "generate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidArgumentException("Kotlin command requires: kotlin generate");

        var output = string.Empty;
        var basePackage = "com.company.d365";
        string? solutionName = null;
        var entities = new List<string>();
        var runKtlintFormat = true;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                case "-o":
                    if (i + 1 >= args.Length) throw new InvalidArgumentException("--out requires a value.");
                    output = args[++i];
                    break;
                case "--package":
                case "-p":
                    if (i + 1 >= args.Length) throw new InvalidArgumentException("--package requires a value.");
                    basePackage = args[++i];
                    break;
                case "--entities":
                    if (i + 1 >= args.Length) throw new InvalidArgumentException("--entities requires a value.");
                    entities = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "--solution":
                case "-s":
                    if (i + 1 >= args.Length) throw new InvalidArgumentException("--solution requires a value.");
                    solutionName = args[++i];
                    break;
                case "--no-ktlint":
                    runKtlintFormat = false;
                    break;
                default:
                    throw new InvalidArgumentException($"Unknown kotlin option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidArgumentException("--out is required for kotlin generation.");

        return CommandExecution.ExecuteWithClient(_logger, client =>
        {
            var generator = new KotlinGenerator(_logger);
            generator.Generate(client, new KotlinGenerationOptions
            {
                OutputPath = output,
                BasePackage = basePackage,
                SolutionName = solutionName,
                Entities = entities,
                RunKtlintFormat = runKtlintFormat,
            });
        });
    }
}
