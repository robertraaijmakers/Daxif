namespace XrmPackager.Core.Generation;

public sealed class ContextGenerationOptions
{
    public required string OutputPath { get; init; }
    public string Namespace { get; init; } = "Xrm.Context";
    public string? SolutionName { get; init; }
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public bool OneFile { get; init; } = true;
}
