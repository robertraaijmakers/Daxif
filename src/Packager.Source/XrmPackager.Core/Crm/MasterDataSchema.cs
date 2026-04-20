namespace XrmPackager.Core.Crm;

using System.Xml.Linq;

public sealed class MasterDataEntitySchema
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string PrimaryIdField { get; init; }
    public required string PrimaryNameField { get; init; }
    public required IReadOnlyList<MasterDataFieldSchema> Fields { get; init; }
    /// <summary>Raw FetchXML string from the schema &lt;filter&gt; element, or null if absent.</summary>
    public string? FetchXmlFilter { get; init; }
}

public sealed class MasterDataFieldSchema
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Type { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool UpdateCompare { get; init; }
    public string? LookupType { get; init; }
}

public static class MasterDataSchemaParser
{
    public static IReadOnlyList<MasterDataEntitySchema> Parse(string schemaPath)
    {
        if (!File.Exists(schemaPath))
        {
            throw new InvalidArgumentException($"Schema file not found: {schemaPath}");
        }

        var doc = XDocument.Load(schemaPath);
        var entities = new List<MasterDataEntitySchema>();

        foreach (var entityEl in doc.Root?.Elements("entity") ?? Enumerable.Empty<XElement>())
        {
            var fields = entityEl
                .Element("fields")
                ?.Elements("field")
                .Select(f => new MasterDataFieldSchema
                {
                    Name =
                        f.Attribute("name")?.Value
                        ?? throw new InvalidOperationException(
                            "Field element is missing required 'name' attribute"
                        ),
                    DisplayName = f.Attribute("displayname")?.Value ?? string.Empty,
                    Type = f.Attribute("type")?.Value ?? "string",
                    IsPrimaryKey = string.Equals(
                        f.Attribute("primaryKey")?.Value,
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    UpdateCompare = string.Equals(
                        f.Attribute("updateCompare")?.Value,
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    LookupType = f.Attribute("lookupType")?.Value,
                })
                .ToList() ?? new List<MasterDataFieldSchema>();

            entities.Add(
                new MasterDataEntitySchema
                {
                    Name =
                        entityEl.Attribute("name")?.Value
                        ?? throw new InvalidOperationException(
                            "Entity element is missing required 'name' attribute"
                        ),
                    DisplayName = entityEl.Attribute("displayname")?.Value ?? string.Empty,
                    PrimaryIdField = entityEl.Attribute("primaryidfield")?.Value ?? string.Empty,
                    PrimaryNameField =
                        entityEl.Attribute("primarynamefield")?.Value ?? string.Empty,
                    Fields = fields,
                    FetchXmlFilter = entityEl.Element("filter")?.Value,
                }
            );
        }

        return entities;
    }
}
