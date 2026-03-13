namespace XrmPackager.Core.Domain;

public record PartyListColumnModel : ColumnModel
{
    public IReadOnlyList<string> TargetTables { get; init; } = [];
}
