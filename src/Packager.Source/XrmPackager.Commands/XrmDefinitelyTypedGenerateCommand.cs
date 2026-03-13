namespace XrmPackager.Commands;

using XrmPackager.Core;
using XrmPackager.Core.Generation;

public sealed class XrmDefinitelyTypedGenerateCommand
{
    private readonly ILogger _logger;

    public XrmDefinitelyTypedGenerateCommand(ILogger logger)
    {
        _logger = logger;
    }

    public int Execute(string[] args)
    {
        if (
            args.Length < 2
            || !string.Equals(args[1], "generate", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidArgumentException("XRMDT command requires: xrmdt generate");
        }

        var parsed = GenerationCommandArgumentParser.Parse(
            args,
            startIndex: 2,
            commandType: GenerationCommandType.XrmDefinitelyTyped,
            defaultNamespace: "Xrm",
            defaultOneFile: false
        );

        return CommandExecution.ExecuteWithClient(
            _logger,
            client =>
            {
                var generator = new TypeScriptContextGenerator(_logger);
                generator.Generate(
                    client,
                    new TypeScriptGenerationOptions
                    {
                        OutputPath = parsed.Output,
                        Namespace = parsed.Namespace,
                        SolutionName = parsed.SolutionName,
                        Entities = parsed.Entities,
                        OneFile = parsed.OneFile,
                        CrmVersion = parsed.CrmVersion,
                        UseDeprecated = parsed.UseDeprecated,
                        SkipForms = parsed.SkipForms,
                        RestNamespace = parsed.RestNamespace,
                        WebNamespace = parsed.WebNamespace,
                        ViewNamespace = parsed.ViewNamespace,
                        JavaScriptLibraryOutputPath = parsed.JavaScriptLibraryOutputPath,
                        TypeScriptLibraryOutputPath = parsed.TypeScriptLibraryOutputPath,
                    }
                );
            }
        );
    }
}
