namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Generation;

public sealed class ContextGenerateCommand
{
    private readonly ILogger _logger;

    public ContextGenerateCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "generate", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidArgumentException("Context command requires: context generate");
        }

        var parsed = GenerationCommandArgumentParser.Parse(args, startIndex: 2, commandType: GenerationCommandType.Context, defaultNamespace: "Xrm.Context", defaultOneFile: true);

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var generator = new CSharpContextGenerator(_logger);
                generator.Generate(
                    client,
                    new ContextGenerationOptions
                    {
                        OutputPath = parsed.Output,
                        Namespace = parsed.Namespace,
                        SolutionName = parsed.SolutionName,
                        Entities = parsed.Entities,
                        OneFile = parsed.OneFile,
                        ConsolidateHelpers = parsed.ConsolidateHelpers,
                        FormatOutput = parsed.FormatOutput,
                    }
                );
            }
        );
    }
}
