namespace XrmPackager.Core.Domain;

public record LookupColumnModel : ColumnModel
{
    public string TargetTable { get; init; } = string.Empty;

    public IReadOnlyList<string> TargetTables { get; init; } = [];

    public string RelationshipName { get; init; } = string.Empty;
}
