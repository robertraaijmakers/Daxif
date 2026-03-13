using XrmPackager.Core.Domain;
using XrmPackager.Core.Generation.Common;
using XrmPackager.Core.Generation.Generators;
using XrmPackager.Core.Generation.Utilities;
using XrmPackager.Core.Templates;

namespace XrmPackager.Core.Generation;

public class CSharpProxyGenerator : ICodeGenerator
{
    private readonly EmbeddedTemplateProvider templateProvider;
    private readonly ProxyClassGenerator proxyClassGenerator;
    private readonly EnumGenerator enumGenerator;
    private readonly IntersectionInterfaceGenerator intersectionInterfaceGenerator;
    private readonly XrmContextGenerator xrmContextGenerator;
    private readonly HelperFileGenerator helperFileGenerator;
    private readonly CustomApiGenerator customApiGenerator;
    private readonly SingleFileGenerator singleFileGenerator;

    public CSharpProxyGenerator()
    {
        templateProvider = new EmbeddedTemplateProvider();
        proxyClassGenerator = new ProxyClassGenerator();
        enumGenerator = new EnumGenerator();
        intersectionInterfaceGenerator = new IntersectionInterfaceGenerator();
        xrmContextGenerator = new XrmContextGenerator();
        helperFileGenerator = new HelperFileGenerator();
        customApiGenerator = new CustomApiGenerator();
        singleFileGenerator = new SingleFileGenerator();
    }

    private static string GetAssemblyVersion()
    {
        var assembly = typeof(CSharpProxyGenerator).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0.0";
    }

    public IEnumerable<GeneratedFile> GenerateCode(
        IEnumerable<TableModel> tables,
        IEnumerable<CustomApiModel> customApis,
        XrmGenerationConfig config
    )
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(config);

        var context = CreateGenerationContext(config);
        var tablesList = NormalizeOptionSetTypeNames(tables.ToList(), config);
        var (interfaceColumns, tableToInterfaces) = PrepareIntersectionData(tablesList, config);
        var customApiList = config.GenerateCustomApis
            ? customApis.ToList()
            : new List<CustomApiModel>();

        if (config.SingleFile)
        {
            var interfaceColumnsReadOnly = interfaceColumns.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<ColumnSignature>)kvp.Value,
                StringComparer.InvariantCulture
            );
            var tableToInterfacesReadOnly = tableToInterfaces.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value,
                StringComparer.InvariantCulture
            );
            return singleFileGenerator.Generate(
                (tablesList, interfaceColumnsReadOnly, tableToInterfacesReadOnly, customApiList),
                context
            );
        }

        return GenerateMultipleFiles(
            tablesList,
            interfaceColumns,
            tableToInterfaces,
            customApiList,
            context
        );
    }

    private static List<TableModel> NormalizeOptionSetTypeNames(
        List<TableModel> tables,
        XrmGenerationConfig config
    )
    {
        var reservedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "DataversePropertyMetadataAttribute",
            "DataversePropertyKind",
            "OptionSetMetadataAttribute",
            "GeneratedOptionSetMetadataAttribute",
            "RelationshipMetadataAttribute",
            "TableAttributeHelpers",
            "ExtendedEntity",
            string.IsNullOrWhiteSpace(config.ServiceContextName)
                ? "Xrm"
                : config.ServiceContextName,
        };

        foreach (var table in tables)
        {
            reservedTypeNames.Add(GenerationUtilities.SanitizeName(table.SchemaName));
        }

        var optionsetNames = tables
            .SelectMany(t => t.Columns)
            .OfType<EnumColumnModel>()
            .Where(c => !string.IsNullOrWhiteSpace(c.OptionsetName))
            .Select(c => c.OptionsetName)
            .Distinct(StringComparer.InvariantCulture)
            .OrderBy(x => x, StringComparer.InvariantCulture)
            .ToList();

        var mappedNames = new Dictionary<string, string>(StringComparer.InvariantCulture);
        var usedTypeNames = new HashSet<string>(reservedTypeNames, StringComparer.Ordinal);

        foreach (var optionsetName in optionsetNames)
        {
            var baseName = GenerationUtilities.SanitizeName(optionsetName, "UnknownOptionSet");
            var candidate = baseName;

            if (usedTypeNames.Contains(candidate))
            {
                candidate = $"{baseName}_Enum";
            }

            var suffix = 1;
            while (usedTypeNames.Contains(candidate))
            {
                candidate = $"{baseName}_Enum_{suffix}";
                suffix++;
            }

            mappedNames[optionsetName] = candidate;
            usedTypeNames.Add(candidate);
        }

        return tables
            .Select(table =>
                table with
                {
                    Columns = table
                        .Columns.Select(column =>
                        {
                            if (column is not EnumColumnModel enumColumn)
                            {
                                return column;
                            }

                            if (
                                !mappedNames.TryGetValue(
                                    enumColumn.OptionsetName,
                                    out var mappedName
                                )
                            )
                            {
                                return enumColumn;
                            }

                            return enumColumn with
                            {
                                OptionsetName = mappedName,
                            };
                        })
                        .ToList(),
                }
            )
            .ToList();
    }

    private GenerationContext CreateGenerationContext(XrmGenerationConfig config)
    {
        return new GenerationContext
        {
            Namespace = config.NamespaceSetting ?? "DataverseContext",
            Version = GetAssemblyVersion(),
            Templates = templateProvider,
            ServiceContextName = string.IsNullOrEmpty(config.ServiceContextName)
                ? "Xrm"
                : config.ServiceContextName,
            IntersectMapping = config.IntersectMapping,
        };
    }

    private static (
        Dictionary<string, HashSet<ColumnSignature>> InterfaceColumns,
        Dictionary<string, List<string>> TableToInterfaces
    ) PrepareIntersectionData(List<TableModel> tablesList, XrmGenerationConfig config)
    {
        var tableDict = tablesList.ToDictionary(
            t => t.LogicalName,
            t => t,
            StringComparer.InvariantCulture
        );
        var tableColumns = BuildTableColumns(tablesList);
        return BuildIntersectionData(config.IntersectMapping, tableDict, tableColumns);
    }

    private IEnumerable<GeneratedFile> GenerateMultipleFiles(
        List<TableModel> tablesList,
        Dictionary<string, HashSet<ColumnSignature>> interfaceColumns,
        Dictionary<string, List<string>> tableToInterfaces,
        IEnumerable<CustomApiModel> customApiList,
        GenerationContext context
    )
    {
        var files = new List<GeneratedFile>();

        // Generate intersection interfaces
        foreach (var kvp in interfaceColumns)
        {
            var interfaceName = kvp.Key;
            var colSigs = kvp.Value;

            files.AddRange(
                intersectionInterfaceGenerator.Generate(
                    (interfaceName, colSigs, tablesList),
                    context
                )
            );
        }

        // Generate proxy classes
        foreach (var table in tablesList)
        {
            var interfaces = tableToInterfaces.TryGetValue(table.LogicalName, out var iFaces)
                ? iFaces
                : new List<string>();
            files.AddRange(proxyClassGenerator.Generate((table, interfaces), context));
        }

        // Generate enums
        var globalOptionsetsMulti = GenerationUtilities.GetGlobalOptionsets(tablesList);
        foreach (var optionset in globalOptionsetsMulti)
        {
            files.AddRange(enumGenerator.Generate(optionset, context));
        }

        // Generate Xrm context class
        files.AddRange(xrmContextGenerator.Generate(tablesList, context));

        // Generate helper files
        files.AddRange(helperFileGenerator.Generate("DataversePropertyMetadataAttribute", context));
        files.AddRange(helperFileGenerator.Generate("OptionSetMetadataAttribute", context));
        files.AddRange(helperFileGenerator.Generate("RelationshipMetadataAttribute", context));
        files.AddRange(helperFileGenerator.Generate("TableAttributeHelpers", context));
        files.AddRange(helperFileGenerator.Generate("ExtendedEntity", context));

        // Generate custom API request/response classes
        foreach (var customApi in customApiList)
        {
            files.AddRange(customApiGenerator.Generate(customApi, context));
        }

        foreach (var file in files)
            yield return file;
    }

    // --- Extracted Helper Methods ---
    private static Dictionary<string, HashSet<ColumnSignature>> BuildTableColumns(
        IEnumerable<TableModel> tables
    )
    {
        var tableColumns = new Dictionary<string, HashSet<ColumnSignature>>(
            StringComparer.InvariantCulture
        );
        foreach (var t in tables)
        {
            var set = new HashSet<ColumnSignature>();
            foreach (var c in t.Columns)
            {
                // For EnumColumnModel, include OptionsetName in the signature to distinguish enums with same logical name but different optionsets
                if (c is EnumColumnModel enumCol)
                    set.Add(
                        new ColumnSignature(
                            c.SchemaName,
                            $"EnumColumnModel:{enumCol.OptionsetName}"
                        )
                    );
                else
                    set.Add(new ColumnSignature(c.SchemaName, c.TypeName));
            }

            tableColumns[t.LogicalName] = set;
        }

        return tableColumns;
    }

    private static (
        Dictionary<string, HashSet<ColumnSignature>> InterfaceColumns,
        Dictionary<string, List<string>> TableToInterfaces
    ) BuildIntersectionData(
        IReadOnlyDictionary<string, IReadOnlyList<string>> intersectMapping,
        Dictionary<string, TableModel> tableDict,
        Dictionary<string, HashSet<ColumnSignature>> tableColumns
    )
    {
        var interfaceColumns = new Dictionary<string, HashSet<ColumnSignature>>(
            StringComparer.InvariantCulture
        );
        var tableToInterfaces = new Dictionary<string, List<string>>(
            StringComparer.InvariantCulture
        );

        if (intersectMapping != null && intersectMapping.Count > 0)
        {
            foreach (var kvp in intersectMapping)
            {
                var interfaceName = kvp.Key;
                var tableNames = kvp.Value.Where(tableDict.ContainsKey).ToList();
                if (tableNames.Count == 0)
                    continue;

                var sets = tableNames.Select(n => tableColumns[n]).ToList();
                var intersection = new HashSet<ColumnSignature>(sets[0]);
                foreach (var s in sets.Skip(1))
                    intersection.IntersectWith(s);

                if (intersection.Count > 0)
                {
                    interfaceColumns[interfaceName] = intersection;
                }

                foreach (var tableName in tableNames)
                {
                    if (!tableToInterfaces.TryGetValue(tableName, out var list))
                    {
                        list = new List<string>();
                        tableToInterfaces[tableName] = list;
                    }

                    if (!list.Contains(interfaceName, StringComparer.InvariantCulture))
                        list.Add(interfaceName);
                }
            }
        }

        return (interfaceColumns, tableToInterfaces);
    }
}
