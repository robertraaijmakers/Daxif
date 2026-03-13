namespace XrmPackager.Core.Crm;

public sealed class WebResourceSyncOptions
{
    public required string FolderPath { get; init; }
    public required string SolutionName { get; init; }
    public bool DeleteMissing { get; init; } = true;
    public bool PublishAfterSync { get; init; } = true;
    public bool DryRun { get; init; }
}
