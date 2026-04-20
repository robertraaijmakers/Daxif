namespace XrmPackager.Core.Crm;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Root of a JSON master-data schema file.
/// </summary>
public sealed class MasterDataJsonConfig
{
    /// <summary>
    /// Fields excluded from every entity retrieval by default.
    /// Overridable per-entity via <see cref="MasterDataJsonEntityConfig.FieldNamesToInclude"/>.
    /// </summary>
    [JsonPropertyName("defaultExcludedFields")]
    public List<string> DefaultExcludedFields { get; set; } = new();

    [JsonPropertyName("entities")]
    public List<MasterDataJsonEntityConfig> Entities { get; set; } = new();

    public static MasterDataJsonConfig Parse(string schemaPath)
    {
        if (!File.Exists(schemaPath))
            throw new InvalidArgumentException($"Schema file not found: {schemaPath}");

        var json = File.ReadAllText(schemaPath);
        return JsonSerializer.Deserialize<MasterDataJsonConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? throw new InvalidOperationException("Failed to parse master data JSON schema.");
    }
}

public sealed class MasterDataJsonEntityConfig
{
    /// <summary>Dataverse logical entity name.</summary>
    [JsonPropertyName("entityName")]
    public required string EntityName { get; set; }

    /// <summary>
    /// Optional folder name override. When set, exported files are placed under
    /// <c>{dataFolder}/{aliasName}/</c> instead of <c>{dataFolder}/{entityName}/</c>.
    /// </summary>
    [JsonPropertyName("aliasName")]
    public string? AliasName { get; set; }
    /// <summary>
    /// The logical name of the primary key field (e.g. <c>accountid</c>).
    /// When set, used during import to identify the record instead of relying on
    /// Dataverse metadata. Leave <c>null</c> for intersect / system entities where
    /// the primary key is inferred from metadata.
    /// </summary>
    [JsonPropertyName("primaryKey")]
    public string? PrimaryKey { get; set; }
    /// <summary>
    /// Explicit list of fields to retrieve. When empty, all fields are retrieved
    /// (minus <see cref="MasterDataJsonConfig.DefaultExcludedFields"/> and <see cref="FieldNamesToExclude"/>).
    /// When set, <see cref="MasterDataJsonConfig.DefaultExcludedFields"/> do not apply — only
    /// <see cref="FieldNamesToExclude"/> is still subtracted.
    /// </summary>
    [JsonPropertyName("fieldNamesToInclude")]
    public List<string> FieldNamesToInclude { get; set; } = new();

    /// <summary>Additional fields to exclude on top of <see cref="MasterDataJsonConfig.DefaultExcludedFields"/>.</summary>
    [JsonPropertyName("fieldNamesToExclude")]
    public List<string> FieldNamesToExclude { get; set; } = new();

    /// <summary>
    /// When <c>true</c>, this entity is excluded from the import step but is still
    /// used to resolve GUID references (Phase 2 of the import). Useful for system
    /// entities (e.g. <c>queue</c>, <c>businessunit</c>) that already exist in the
    /// target environment and must not be modified, but are referenced by other entities.
    /// Defaults to <c>false</c>.
    /// </summary>
    [JsonPropertyName("referenceOnly")]
    public bool ReferenceOnly { get; set; } = false;

    /// <summary>
    /// Optional filter. Two formats are accepted:
    /// <list type="bullet">
    ///   <item>FetchXML string (starts with <c>&lt;</c>) — used directly.</item>
    ///   <item>OData filter expression — converted to FetchXML automatically for simple conditions.</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    // ── computed helpers ──────────────────────────────────────────────────────

    public string FolderName => AliasName ?? EntityName;

    public bool HasFilter => !string.IsNullOrWhiteSpace(Filter);

    public bool IsFetchXmlFilter => HasFilter && Filter!.TrimStart().StartsWith('<');

    /// <summary>
    /// Returns the effective include / exclude sets given the schema-level default excludes.
    /// <para><c>explicitIncludes == null</c> means "all fields" (apply <paramref name="defaultExcludes"/>).</para>
    /// </summary>
    public (HashSet<string>? explicitIncludes, HashSet<string> effectiveExcludes) ResolveFields(
        IEnumerable<string> defaultExcludes
    )
    {
        // entity-level excludes always apply
        var excludes = new HashSet<string>(
            FieldNamesToExclude,
            StringComparer.OrdinalIgnoreCase
        );

        if (FieldNamesToInclude.Count > 0)
        {
            // Explicit include: default excludes don't apply; entity excludes still do
            var includes = new HashSet<string>(FieldNamesToInclude, StringComparer.OrdinalIgnoreCase);
            foreach (var f in FieldNamesToExclude)
                includes.Remove(f);
            return (includes, excludes);
        }

        // "All fields": merge default + entity-level excludes
        foreach (var f in defaultExcludes)
            excludes.Add(f);
        return (null, excludes);
    }
}
