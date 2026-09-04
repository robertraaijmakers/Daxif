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

    public static string Build(TableModel table, string basePackage)
    {
        var entityPackage = $"{basePackage}.entity";
        var corePackage = $"{basePackage}.core";

        var entitySetName = !string.IsNullOrWhiteSpace(table.EntitySetName)
            ? table.EntitySetName
            : Pluralize(table.LogicalName);

        var entityName = KotlinNameHelper.ToClassName(table.SchemaName) + "Entity";

        var properties = BuildProperties(table);
        var needsBigDecimal = properties.Any(p => p.KotlinType.Contains("BigDecimal"));
        var needsUuid = properties.Any(p => p.KotlinType.Contains("UUID"));
        var needsD365Field = properties.Any(p => p.HasD365FieldAnnotation);

        var sb = new StringBuilder();
        sb.AppendLine($"package {entityPackage}");
        sb.AppendLine();
        sb.AppendLine("import com.fasterxml.jackson.annotation.JsonInclude");
        sb.AppendLine("import com.fasterxml.jackson.annotation.JsonProperty");
        sb.AppendLine($"import {corePackage}.D365Entity");
        if (needsD365Field)
        {
            sb.AppendLine($"import {corePackage}.D365Field");
            sb.AppendLine($"import {corePackage}.D365FieldKind");
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
        sb.AppendLine($"class {entityName} {{");

        // etag: read-only, never sent in write bodies
        sb.AppendLine("    @field:JsonProperty(value = \"@odata.etag\", access = JsonProperty.Access.READ_ONLY)");
        sb.AppendLine("    var etag: String? = null");

        foreach (var prop in properties)
        {
            sb.AppendLine();
            sb.AppendLine(prop.Code);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed record PropertyEntry(string Code, string KotlinType, bool HasD365FieldAnnotation);

    private static List<PropertyEntry> BuildProperties(TableModel table)
    {
        var result = new List<PropertyEntry>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal) { "etag" };

        foreach (var column in table.Columns)
        {
            result.AddRange(BuildPropertyEntries(column, usedNames));
        }

        return result;
    }

    private static IEnumerable<PropertyEntry> BuildPropertyEntries(ColumnModel column, HashSet<string> usedNames)
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
                yield return new PropertyEntry(
                    $"    @D365Field(logicalName = \"{KotlinNameHelper.EscapeString(column.LogicalName)}\", kind = D365FieldKind.{kind})\n" +
                    $"    @field:JsonProperty(\"{KotlinNameHelper.EscapeString(column.LogicalName)}\")\n" +
                    $"    var {propName}: {kotlinType} = null",
                    kotlinType, true);
                break;
            }

            case LookupColumnModel lookupCol:
            {
                usedNames.Remove(propName);

                var isSystemManaged = SystemManagedLookupLogicalNames.Contains(column.LogicalName);
                var isPolymorphic = lookupCol.TargetTables.Count > 1;

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
                    if (isPolymorphic)
                    {
                        foreach (var target in lookupCol.TargetTables)
                        {
                            var bindName = Unique(propName + KotlinNameHelper.ToClassName(target) + "Bind", usedNames);
                            usedNames.Add(bindName);
                            yield return new PropertyEntry(
                                $"    @field:JsonProperty(value = \"{KotlinNameHelper.EscapeString(column.SchemaName)}_{KotlinNameHelper.EscapeString(target)}@odata.bind\", access = JsonProperty.Access.WRITE_ONLY)\n" +
                                $"    var {bindName}: String? = null",
                                "String", false);
                        }
                    }
                    else
                    {
                        var bindName = Unique(propName + "Bind", usedNames);
                        usedNames.Add(bindName);
                        yield return new PropertyEntry(
                            $"    @field:JsonProperty(value = \"{KotlinNameHelper.EscapeString(column.SchemaName)}@odata.bind\", access = JsonProperty.Access.WRITE_ONLY)\n" +
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
