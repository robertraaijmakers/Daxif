namespace XrmPackager.Core.Generation.Kotlin;

using System.Text;
using XrmPackager.Core.Domain;

public static class KotlinEntityCodeBuilder
{
    // Lookup logical names that D365 OData rejects writes to; keep value field, skip bind field.
    private static readonly HashSet<string> SystemManagedLookupLogicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdby",
        "modifiedby",
        "createdonbehalfby",
        "modifiedonbehalfby",
        "owningbusinessunit",
        "owningteam",
        "owninguser",
    };

    // Fields declared in D365BaseEntity — skipped when the entity extends it.
    private static readonly HashSet<string> BaseEntityLogicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "versionnumber",
        "createdon",
        "modifiedon",
        "overriddencreatedon",
        "createdby",
        "modifiedby",
        "createdonbehalfby",
        "modifiedonbehalfby",
    };

    // Fields declared in D365OwnableEntity — skipped when the entity extends it.
    private static readonly HashSet<string> OwnableEntityLogicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ownerid",
        "owningbusinessunit",
        "owningteam",
        "owninguser",
    };

    public static string Build(TableModel table, string basePackage, IReadOnlySet<string> generatedEntitySchemaNames)
    {
        var entityPackage = $"{basePackage}.entity";
        var corePackage = $"{basePackage}.core";

        var entitySetName = !string.IsNullOrWhiteSpace(table.EntitySetName)
            ? table.EntitySetName
            : Pluralize(table.LogicalName);

        var entityName = KotlinNameHelper.ToClassName(table.SchemaName) + "Entity";

        var columnLogicalNames = table.Columns.Select(c => c.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasOwnerid = columnLogicalNames.Contains("ownerid");
        var hasCreatedon = columnLogicalNames.Contains("createdon");

        string? baseClass = hasOwnerid ? "D365OwnableEntity"
            : hasCreatedon ? "D365BaseEntity"
            : null;

        var skipLogicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (baseClass != null) skipLogicalNames.UnionWith(BaseEntityLogicalNames);
        if (baseClass == "D365OwnableEntity") skipLogicalNames.UnionWith(OwnableEntityLogicalNames);

        var properties = BuildProperties(table, generatedEntitySchemaNames, skipLogicalNames);
        var needsBigDecimal = properties.Any(p => p.KotlinType.Contains("BigDecimal"));
        var needsUuid = properties.Any(p => p.KotlinType.Contains("UUID"));
        var needsD365Field = properties.Any(p => p.HasD365FieldAnnotation);
        var needsMultiOptionSet = properties.Any(p => p.IsMultiOptionSet);

        var sb = new StringBuilder();
        sb.AppendLine($"package {entityPackage}");
        sb.AppendLine();
        sb.AppendLine("import com.fasterxml.jackson.annotation.JsonInclude");
        sb.AppendLine("import com.fasterxml.jackson.annotation.JsonProperty");
        sb.AppendLine($"import {corePackage}.D365Entity");
        if (baseClass != null) sb.AppendLine($"import {corePackage}.{baseClass}");
        if (needsD365Field)
        {
            sb.AppendLine($"import {corePackage}.D365Field");
            sb.AppendLine($"import {corePackage}.D365FieldKind");
        }
        if (needsMultiOptionSet)
        {
            sb.AppendLine($"import {corePackage}.D365MultiOptionSetDeserializer");
            sb.AppendLine("import tools.jackson.databind.annotation.JsonDeserialize");
        }
        if (needsBigDecimal) sb.AppendLine("import java.math.BigDecimal");
        if (needsUuid) sb.AppendLine("import java.util.UUID");
        sb.AppendLine();
        sb.AppendLine("@D365Entity(");
        sb.AppendLine($"    logicalName = \"{KotlinNameHelper.EscapeString(table.LogicalName)}\",");
        sb.AppendLine($"    entitySetName = \"{KotlinNameHelper.EscapeString(entitySetName)}\",");
        sb.AppendLine($"    primaryKey = \"{KotlinNameHelper.EscapeString(table.PrimaryIdAttribute)}\",");
        sb.AppendLine($"    primaryName = \"{KotlinNameHelper.EscapeString(table.PrimaryNameAttribute ?? string.Empty)}\"");
        sb.AppendLine(")");
        sb.AppendLine("@JsonInclude(JsonInclude.Include.NON_NULL)");

        var classDeclaration = baseClass != null
            ? $"class {entityName} : {baseClass}() {{"
            : $"class {entityName} {{";
        sb.AppendLine(classDeclaration);

        if (baseClass == null)
        {
            sb.AppendLine("    @field:JsonProperty(value = \"@odata.etag\", access = JsonProperty.Access.READ_ONLY)");
            sb.AppendLine("    var etag: String? = null");
        }

        foreach (var prop in properties)
        {
            sb.AppendLine();
            sb.AppendLine(prop.Code);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed record PropertyEntry(string Code, string KotlinType, bool HasD365FieldAnnotation, bool IsMultiOptionSet = false);

    private static List<PropertyEntry> BuildProperties(
        TableModel table,
        IReadOnlySet<string> generatedEntitySchemaNames,
        IReadOnlySet<string> skipLogicalNames)
    {
        var result = new List<PropertyEntry>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal) { "etag" };

        // Map attribute logical name → ManyToOne relationships (one per target entity)
        // Value is the ReferencingEntityNavigationPropertyName — the single-valued nav prop used in @odata.bind
        var lookupRelationships = table.Relationships
            .Where(r => r.RelationshipType == "ManyToOne" && !string.IsNullOrWhiteSpace(r.ThisEntityAttribute))
            .GroupBy(r => r.ThisEntityAttribute!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    r => r.RelatedEntity ?? string.Empty,
                    r => r.NavigationPropertyName ?? r.SchemaName,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var column in table.Columns
            .Where(c => !skipLogicalNames.Contains(c.LogicalName))
            .OrderBy(c => c.LogicalName, StringComparer.OrdinalIgnoreCase))
        {
            result.AddRange(BuildPropertyEntries(column, usedNames, lookupRelationships));
        }

        // Collection-valued navigation properties for OneToMany relationships (deep insert / write-only)
        foreach (var rel in table.Relationships
            .Where(r => r.RelationshipType == "OneToMany"
                && r.RelatedEntitySchemaName != null
                && generatedEntitySchemaNames.Contains(r.RelatedEntitySchemaName))
            .DistinctBy(r => r.NavigationPropertyName ?? r.SchemaName)
            .OrderBy(r => r.NavigationPropertyName ?? r.SchemaName, StringComparer.OrdinalIgnoreCase))
        {
            var navProp = rel.NavigationPropertyName ?? rel.SchemaName;
            if (string.IsNullOrWhiteSpace(navProp)) continue;

            var propName = Unique(KotlinNameHelper.ToPropertyName(navProp), usedNames);
            usedNames.Add(propName);

            var relatedClass = KotlinNameHelper.ToClassName(rel.RelatedEntitySchemaName!) + "Entity";
            result.Add(new PropertyEntry(
                $"    @field:JsonProperty(value = \"{KotlinNameHelper.EscapeString(navProp)}\", access = JsonProperty.Access.WRITE_ONLY)\n" +
                $"    var {propName}: List<{relatedClass}>? = null",
                $"List<{relatedClass}>", false));
        }

        return result;
    }

    private static IEnumerable<PropertyEntry> BuildPropertyEntries(
        ColumnModel column,
        HashSet<string> usedNames,
        Dictionary<string, Dictionary<string, string>> lookupRelationships)
    {
        var basePropName = KotlinNameHelper.ToPropertyName(column.SchemaName);
        var propName = Unique(basePropName, usedNames);
        usedNames.Add(propName);

        switch (column)
        {
            case PrimaryIdColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.GUID, readOnly = true)\n" +
                    $"    @field:JsonProperty(value = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", access = JsonProperty.Access.READ_ONLY)\n" +
                    $"    var {propName}: UUID? = null",
                    "UUID", true);
                break;

            case StringColumnModel or MemoColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.STRING)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: String? = null",
                    "String", true);
                break;

            case IntegerColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.INTEGER)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: Int? = null",
                    "Int", true);
                break;

            case BigIntColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.INTEGER)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: Long? = null",
                    "Long", true);
                break;

            case BooleanColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.BOOLEAN)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: Boolean? = null",
                    "Boolean", true);
                break;

            case DateTimeColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.DATETIME)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: String? = null",
                    "String", true);
                break;

            case DecimalColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.DECIMAL)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: BigDecimal? = null",
                    "BigDecimal", true);
                break;

            case DoubleColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.DECIMAL)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: Double? = null",
                    "Double", true);
                break;

            case MoneyColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.MONEY)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: BigDecimal? = null",
                    "BigDecimal", true);
                break;

            case EnumColumnModel enumCol:
            {
                var kotlinType = enumCol.IsMultiSelect ? "List<Int>?" : "Int?";
                var kind = enumCol.IsMultiSelect ? "MULTI_OPTION_SET" : "OPTION_SET";
                var multiDeserialize = enumCol.IsMultiSelect
                    ? $"\n    @field:JsonDeserialize(using = D365MultiOptionSetDeserializer::class)"
                    : "";
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.{kind})\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\"){multiDeserialize}\n" +
                    $"    var {propName}: {kotlinType} = null",
                    kotlinType, true, enumCol.IsMultiSelect);
                break;
            }

            case LookupColumnModel lookupCol:
            {
                usedNames.Remove(propName);

                var isSystemManaged = SystemManagedLookupLogicalNames.Contains(column.LogicalName);
                var isPolymorphic = lookupCol.TargetTables.Count > 1;
                lookupRelationships.TryGetValue(column.LogicalName, out var relsByTarget);

                // Read: _logicalname_value (always present)
                var readName = Unique(propName + "Value", usedNames);
                usedNames.Add(readName);
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.LOOKUP, readOnly = true)\n" +
                    $"    @field:JsonProperty(value = \"_{KotlinNameHelper.EscapeString(column.LogicalName)}_value\", access = JsonProperty.Access.READ_ONLY)\n" +
                    $"    var {readName}: UUID? = null",
                    "UUID", true);

                // Polymorphic logicalname annotation field
                if (isPolymorphic)
                {
                    var logicalNameField = Unique(propName + "LogicalName", usedNames);
                    usedNames.Add(logicalNameField);
                    yield return new PropertyEntry(
                        $"    @field:JsonProperty(value = \"_{KotlinNameHelper.EscapeString(column.LogicalName)}_value@Microsoft.Dynamics.CRM.lookuplogicalname\", access = JsonProperty.Access.READ_ONLY)\n" +
                        $"    var {logicalNameField}: String? = null",
                        "String", false);
                }

                // Write bind fields — skip for system-managed lookups
                if (!isSystemManaged)
                {
                    var targets = isPolymorphic ? lookupCol.TargetTables : [lookupCol.TargetTable];

                    // Resolve navProp per target, then deduplicate: when multiple targets share the
                    // same bind path (same navProp), emit only one field with the simple "Bind" suffix.
                    var navPropTargets = targets
                        .Select(t =>
                        {
                            var navProp = relsByTarget != null && relsByTarget.TryGetValue(t, out var relSchema) && !string.IsNullOrWhiteSpace(relSchema)
                                ? relSchema
                                : isPolymorphic
                                    ? $"{column.LogicalName}_{t}"
                                    : column.LogicalName;
                            return (Target: t, NavProp: navProp);
                        })
                        .DistinctBy(x => x.NavProp, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var multipleBindPaths = navPropTargets.Count > 1;

                    foreach (var (target, navProp) in navPropTargets)
                    {
                        var suffix = multipleBindPaths ? KotlinNameHelper.ToClassName(target) + "Bind" : "Bind";
                        var bindName = Unique(propName + suffix, usedNames);
                        usedNames.Add(bindName);
                        yield return new PropertyEntry(
                            $"    @field:JsonProperty(value = \"{KotlinNameHelper.EscapeString(navProp)}@odata.bind\", access = JsonProperty.Access.WRITE_ONLY)\n" +
                            $"    var {bindName}: String? = null",
                            "String", false);
                    }
                }

                break;
            }

            case UniqueIdentifierColumnModel:
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.GUID)\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: UUID? = null",
                    "UUID", true);
                break;

            // Skip: PartyListColumnModel, FileColumnModel, ImageColumnModel, ManagedColumnModel, BooleanManagedColumnModel
        }
    }

    private static string Pluralize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name + "s";
        if (name.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("z", StringComparison.OrdinalIgnoreCase))
            return name + "es";
        return name + "s";
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (!used.Contains(name)) return name;
        var i = 1;
        while (used.Contains(name + i)) i++;
        return name + i;
    }
}
