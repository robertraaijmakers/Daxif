namespace XrmPackager.Core.Crm;

public sealed class SolutionImportOptions
{
    public required string ZipPath { get; init; }
    public bool PublishAfterImport { get; init; }
    public bool OverwriteUnmanagedCustomizations { get; init; }
    public bool SkipProductUpdateDependencies { get; init; }
    public bool ConvertToManaged { get; init; }
}
