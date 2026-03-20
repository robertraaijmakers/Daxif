namespace XrmPackager.Commands;

using XrmPackager.Core;

internal enum GenerationCommandType
{
    Context,
    XrmDefinitelyTyped,
}

internal sealed class GenerationCommandArguments
{
    public required string Output { get; init; }
    public required string Namespace { get; init; }
    public string? SolutionName { get; init; }
    public required List<string> Entities { get; init; }
    public bool OneFile { get; init; }
    public bool ConsolidateHelpers { get; init; }
    public bool FormatOutput { get; init; }

    public string? CrmVersion { get; init; }
    public bool UseDeprecated { get; init; }
    public bool SkipForms { get; init; }
    public string? RestNamespace { get; init; }
    public string? WebNamespace { get; init; }
    public string? ViewNamespace { get; init; }
    public string? JavaScriptLibraryOutputPath { get; init; }
    public string? TypeScriptLibraryOutputPath { get; init; }
}

internal static class GenerationCommandArgumentParser
{
    public static GenerationCommandArguments Parse(string[] args, int startIndex, GenerationCommandType commandType, string defaultNamespace, bool defaultOneFile)
    {
        var output = string.Empty;
        var ns = defaultNamespace;
        string? solutionName = null;
        var entities = new List<string>();
        var oneFile = defaultOneFile;
        var consolidateHelpers = false;
        var formatOutput = false;

        string? crmVersion = null;
        var useDeprecated = false;
        var skipForms = false;
        string? restNamespace = null;
        string? webNamespace = null;
        string? viewNamespace = null;
        string? jsLibOutput = null;
        string? tsLibOutput = null;

        for (var i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                case "-o":
                    output = ReadRequiredValue(args, ref i, "--out");
                    break;
                case "--namespace":
                case "-n":
                    ns = ReadRequiredValue(args, ref i, "--namespace");
                    break;
                case "--entities":
                    entities = ReadRequiredValue(args, ref i, "--entities").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "--solution":
                case "-s":
                    solutionName = ReadRequiredValue(args, ref i, "--solution");
                    break;
                case "--multi-file":
                    oneFile = false;
                    break;
                case "--one-file":
                    oneFile = true;
                    break;
                case "--consolidate-helpers":
                    consolidateHelpers = true;
                    break;
                case "--format":
                    formatOutput = true;
                    break;

                case "--crm-version":
                case "--cv":
                    EnsureSupported(args[i], commandType);
                    crmVersion = ReadRequiredValue(args, ref i, "--crm-version");
                    break;
                case "--use-deprecated":
                case "--ud":
                    EnsureSupported(args[i], commandType);
                    useDeprecated = true;
                    break;
                case "--skip-forms":
                case "--sf":
                    EnsureSupported(args[i], commandType);
                    skipForms = true;
                    break;
                case "--rest":
                case "-r":
                    EnsureSupported(args[i], commandType);
                    restNamespace = ReadRequiredValue(args, ref i, "--rest");
                    break;
                case "--web":
                case "-w":
                    EnsureSupported(args[i], commandType);
                    webNamespace = ReadRequiredValue(args, ref i, "--web");
                    break;
                case "--views":
                case "-v":
                    EnsureSupported(args[i], commandType);
                    viewNamespace = ReadRequiredValue(args, ref i, "--views");
                    break;
                case "--jslib":
                case "--jl":
                    EnsureSupported(args[i], commandType);
                    jsLibOutput = ReadRequiredValue(args, ref i, "--jslib");
                    break;
                case "--tslib":
                case "--tl":
                    EnsureSupported(args[i], commandType);
                    tsLibOutput = ReadRequiredValue(args, ref i, "--tslib");
                    break;

                default:
                    throw commandType == GenerationCommandType.Context
                        ? new InvalidArgumentException($"Unknown context option: {args[i]}")
                        : new InvalidArgumentException($"Unknown xrmdt option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw commandType == GenerationCommandType.Context
                ? new InvalidArgumentException("--out is required for context generation.")
                : new InvalidArgumentException("--out is required for xrmdt generation.");
        }

        return new GenerationCommandArguments
        {
            Output = output,
            Namespace = ns,
            SolutionName = solutionName,
            Entities = entities,
            OneFile = oneFile,
            ConsolidateHelpers = consolidateHelpers,
            FormatOutput = formatOutput,
            CrmVersion = crmVersion,
            UseDeprecated = useDeprecated,
            SkipForms = skipForms,
            RestNamespace = restNamespace,
            WebNamespace = webNamespace,
            ViewNamespace = viewNamespace,
            JavaScriptLibraryOutputPath = jsLibOutput,
            TypeScriptLibraryOutputPath = tsLibOutput,
        };
    }

    private static void EnsureSupported(string option, GenerationCommandType commandType)
    {
        if (commandType == GenerationCommandType.XrmDefinitelyTyped)
        {
            return;
        }

        throw new InvalidArgumentException($"Unknown context option: {option}");
    }

    private static string ReadRequiredValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidArgumentException($"{option} requires a value.");
        }

        return args[++index];
    }
}
