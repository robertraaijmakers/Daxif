namespace XrmPackager.Core.Generation;

public sealed class TypeScriptGenerationOptions
{
    public required string OutputPath { get; init; }
    public string Namespace { get; init; } = "Xrm";
    public string? SolutionName { get; init; }
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public bool OneFile { get; init; } = false;
    public string? CrmVersion { get; init; }
    public bool UseDeprecated { get; init; }
    public bool SkipForms { get; init; }
    public string? RestNamespace { get; init; }
    public string? WebNamespace { get; init; }
    public string? ViewNamespace { get; init; }
    public string? JavaScriptLibraryOutputPath { get; init; }
    public string? TypeScriptLibraryOutputPath { get; init; }
    public bool EmitLegacyResources { get; init; } = true;
}
