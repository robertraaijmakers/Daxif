namespace XrmPackager.Core.Generation.Kotlin;

public sealed class KotlinGenerationOptions
{
    public required string OutputPath { get; init; }
    public string BasePackage { get; init; } = "com.company.d365";
    public string? SolutionName { get; init; }
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public bool RunKtlintFormat { get; init; } = true;
}
