namespace XrmPackager.Core.Domain;

public record MemoColumnModel : ColumnModel
{
    public int? MaxLength { get; init; }
}
