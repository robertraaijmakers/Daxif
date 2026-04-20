namespace XrmPackager.Core.Crm;

public sealed class MasterDataImportOptions
{
    public required string SchemaPath { get; init; }
    public required string DataFolder { get; init; }

    /// <summary>
    /// When <c>true</c>, all read operations (metadata, GUID resolution, change detection)
    /// are performed but no records are created or updated in Dataverse.
    /// </summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, logs each field that triggered an update for every record
    /// that has changes. Most useful combined with <see cref="DryRun"/>.
    /// </summary>
    public bool LogChanges { get; init; } = false;
}
