namespace XrmPackager.Core.Generation;

using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmPackager.Core.Domain;
using XrmPackager.Core.Metadata;

internal static class TypeScriptLegacyArtifactGenerator
{
    private const string ResourcePrefix = "XrmPackager.Core.Templates.TypeScript.";

    private static readonly Dictionary<string, FormControlKind> ControlClassMap = new(
        StringComparer.InvariantCultureIgnoreCase
    )
    {
        ["B0C6723A-8503-4FD7-BB28-C8A06AC933C2"] = FormControlKind.OptionSet,
        ["5B773807-9FB2-42DB-97C3-7A91EFF8ADFF"] = FormControlKind.Date,
        ["C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E"] = FormControlKind.Number,
        ["AA987274-CE4E-4271-A803-66164311A958"] = FormControlKind.Number,
        ["ADA2203E-B4CD-49BE-9DDF-234642B43B52"] = FormControlKind.String,
        ["6F3FB987-393B-4D2D-859F-9D0F0349B6AD"] = FormControlKind.Default,
        ["0D2C745A-E5A8-4C8F-BA63-C6D3BB604660"] = FormControlKind.Number,
        ["FD2A7985-3187-444E-908D-6624B21F69C0"] = FormControlKind.IFrame,
        ["C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F"] = FormControlKind.Number,
        ["270BD3DB-D9AF-4782-9025-509E298DEC0A"] = FormControlKind.Lookup,
        ["533B9E00-756B-4312-95A0-DC888637AC78"] = FormControlKind.Number,
        ["06375649-C143-495E-A496-C962E5B4488E"] = FormControlKind.Default,
        ["E0DECE4B-6FC8-4A8F-A065-082708572369"] = FormControlKind.String,
        ["4273EDBD-AC1D-40D3-9FB2-095C621B552D"] = FormControlKind.String,
        ["71716B6C-711E-476C-8AB8-5D11542BFB47"] = FormControlKind.String,
        ["1E1FC551-F7A8-43AF-AC34-A8DC35C7B6D4"] = FormControlKind.String,
        ["671A9387-CA5A-4D1E-8AB7-06E39DDCF6B5"] = FormControlKind.OptionSet,
        ["7C624A0B-F59E-493D-9583-638D34759266"] = FormControlKind.OptionSet,
        ["CBFB742C-14E7-4A17-96BB-1A13F7F64AA2"] = FormControlKind.Lookup,
        ["3EF39988-22BB-4F0B-BBBE-64B5A3748AEE"] = FormControlKind.OptionSet,
        ["67FAC785-CD58-4F9F-ABB3-4B7DDC6ED5ED"] = FormControlKind.OptionSet,
        ["F3015350-44A2-4AA0-97B5-00166532B5E9"] = FormControlKind.Lookup,
        ["5D68B988-0661-4DB2-BC3E-17598AD3BE6C"] = FormControlKind.MultiSelectOptionSet,
        ["5C5600E0-1D6E-4205-A272-BE80DA87FD42"] = FormControlKind.QuickView,
        ["E7A81278-8635-4D9E-8D4D-59480B391C5B"] = FormControlKind.SubGrid,
        ["E616A57F-20E0-4534-8662-A101B5DDF4E0"] = FormControlKind.KnowledgeBaseSearch,
    };

    public static void Generate(
        string outputDirectory,
        ServiceClient client,
        TypeScriptGenerationOptions options,
        IReadOnlyList<TableModel> tables,
        ILogger logger
    )
    {
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "_internal"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "_internal", "Enum"));

        WriteCoreDts(outputDirectory, options, logger);
        WriteEnumFiles(outputDirectory, tables);
        WriteLcidEnumFile(outputDirectory, tables);
        WriteWebResourcesFile(outputDirectory, client);

        if (!string.IsNullOrWhiteSpace(options.WebNamespace))
        {
            WriteWebEntityFiles(outputDirectory, options.WebNamespace, tables);
        }

        if (!string.IsNullOrWhiteSpace(options.RestNamespace))
        {
            WriteRestEntityFiles(outputDirectory, options.RestNamespace, tables);
        }

        if (!options.SkipForms)
        {
            WriteFormFiles(outputDirectory, tables, client);
        }

        if (!string.IsNullOrWhiteSpace(options.ViewNamespace))
        {
            WriteViewFiles(outputDirectory, options.ViewNamespace, tables);
        }

        if (!string.IsNullOrWhiteSpace(options.JavaScriptLibraryOutputPath))
        {
            WriteJavaScriptLibraries(options.JavaScriptLibraryOutputPath, options);
        }

        if (!string.IsNullOrWhiteSpace(options.TypeScriptLibraryOutputPath))
        {
            WriteTypeScriptLibraries(options.TypeScriptLibraryOutputPath, options);
        }
    }

    private static void WriteCoreDts(
        string outputDirectory,
        TypeScriptGenerationOptions options,
        ILogger logger
    )
    {
        WriteResourceDirect("xrm.d.ts", Path.Combine(outputDirectory, "xrm.d.ts"));

        WriteResourceDirect("metadata.d.ts", Path.Combine(outputDirectory, "metadata.d.ts"));
        WriteResourceDirect(
            "_internal/sdk.d.ts",
            Path.Combine(outputDirectory, "_internal", "sdk.d.ts")
        );

        if (!string.IsNullOrWhiteSpace(options.WebNamespace))
        {
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.web.d.ts",
                Path.Combine(outputDirectory, "dg.xrmquery.web.d.ts")
            );
        }

        if (!string.IsNullOrWhiteSpace(options.RestNamespace))
        {
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.rest.d.ts",
                Path.Combine(outputDirectory, "dg.xrmquery.rest.d.ts")
            );
        }

        logger.Info("Legacy .d.ts resource files generated.");
    }

    private static void WriteEnumFiles(string outputDirectory, IReadOnlyList<TableModel> tables)
    {
        var enumColumns = tables.SelectMany(t => t.Columns).OfType<EnumColumnModel>().ToList();
        var grouped = enumColumns
            .GroupBy(e => e.OptionsetName, StringComparer.InvariantCulture)
            .OrderBy(g => g.Key, StringComparer.InvariantCulture);

        foreach (var group in grouped)
        {
            var name = SanitizeIdentifier(group.Key, "OptionSet");
            var filePath = Path.Combine(outputDirectory, "_internal", "Enum", $"{name}.d.ts");
            var options = group
                .SelectMany(g => g.OptionsetValues)
                .DistinctBy(v => v.Key)
                .OrderBy(v => v.Key);

            var lines = new List<string> { $"declare const enum {name} {{" };

            foreach (var option in options)
            {
                lines.Add(
                    $"  {SanitizeIdentifier(option.Value, $"Value{option.Key}")} = {option.Key},"
                );
            }

            lines.Add("}");
            File.WriteAllLines(filePath, lines);
        }
    }

    private static void WriteLcidEnumFile(string outputDirectory, IReadOnlyList<TableModel> tables)
    {
        var lcids = tables
            .SelectMany(t => t.Columns)
            .OfType<EnumColumnModel>()
            .SelectMany(c => c.OptionLocalizations.Values)
            .SelectMany(m => m.Keys)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (lcids.Count == 0)
        {
            lcids.Add(1033);
        }

        var lines = new List<string> { "declare const enum LCID {" };
        foreach (var lcid in lcids)
        {
            lines.Add($"  LCID_{lcid} = {lcid},");
        }

        lines.Add("}");
        File.WriteAllLines(Path.Combine(outputDirectory, "_internal", "Enum", "LCID.d.ts"), lines);
    }

    private static void WriteWebResourcesFile(string outputDirectory, IOrganizationService service)
    {
        var query = new QueryExpression("webresource") { ColumnSet = new ColumnSet("name") };

        query.Criteria.AddCondition(
            new ConditionExpression("webresourcetype", ConditionOperator.In, [5, 6, 7])
        );

        var names = service
            .RetrieveMultiple(query)
            .Entities.Select(e => e.GetAttributeValue<string>("name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.InvariantCulture)
            .OrderBy(n => n, StringComparer.InvariantCulture)
            .ToArray();

        List<string> lines;
        if (names.Length == 0)
        {
            lines = ["declare type WebResourceImage = undefined;"];
        }
        else
        {
            lines = [$"declare type WebResourceImage = \"{names[0]}\""];
            foreach (var name in names.Skip(1))
            {
                lines.Add($"    | \"{name}\"");
            }
        }

        File.WriteAllLines(Path.Combine(outputDirectory, "_internal", "WebResources.d.ts"), lines);
    }

    private static void WriteWebEntityFiles(
        string outputDirectory,
        string webNamespace,
        IReadOnlyList<TableModel> tables
    )
    {
        var webDir = Path.Combine(outputDirectory, "Web");
        Directory.CreateDirectory(webDir);

        var internalDefs = new List<string>
        {
            "// <auto-generated />",
            $"declare namespace {webNamespace} {{",
            "  interface WebEntitiesRetrieve {",
        };

        foreach (var table in tables.OrderBy(t => t.LogicalName, StringComparer.InvariantCulture))
        {
            var safeSchema = SanitizeIdentifier(table.SchemaName, "Entity");
            var filePath = Path.Combine(webDir, $"{table.LogicalName}.d.ts");
            var lines = new List<string>
            {
                "// <auto-generated />",
                $"declare namespace {webNamespace} {{",
                $"  interface {safeSchema}_Base {{",
            };

            foreach (
                var column in table.Columns.OrderBy(
                    c => c.LogicalName,
                    StringComparer.InvariantCulture
                )
            )
            {
                lines.Add($"    {column.LogicalName}?: {MapTsType(column)};");
            }

            lines.Add("  }");
            lines.Add($"  interface {safeSchema}_Result extends {safeSchema}_Base {{}}");
            lines.Add("}");
            File.WriteAllLines(filePath, lines);

            internalDefs.Add($"    {table.LogicalName}: {safeSchema}_Result;");
        }

        internalDefs.Add("  }");
        internalDefs.Add("}");
        File.WriteAllLines(
            Path.Combine(outputDirectory, "_internal", "web-entities.d.ts"),
            internalDefs
        );
    }

    private static void WriteRestEntityFiles(
        string outputDirectory,
        string restNamespace,
        IReadOnlyList<TableModel> tables
    )
    {
        var restDir = Path.Combine(outputDirectory, "REST");
        Directory.CreateDirectory(restDir);

        var restRootLines = new List<string>
        {
            "// <auto-generated />",
            $"declare namespace {restNamespace} {{",
            "  interface RestEntities {",
        };

        var internalLines = new List<string>
        {
            "// <auto-generated />",
            $"declare namespace {restNamespace} {{",
            "  interface RestEntityMap {",
        };

        foreach (var table in tables.OrderBy(t => t.LogicalName, StringComparer.InvariantCulture))
        {
            var safeSchema = SanitizeIdentifier(table.SchemaName, "Entity");
            var filePath = Path.Combine(restDir, $"{table.LogicalName}.d.ts");
            var lines = new List<string>
            {
                "// <auto-generated />",
                $"declare namespace {restNamespace} {{",
                $"  interface {safeSchema} {{",
            };

            foreach (
                var column in table.Columns.OrderBy(
                    c => c.LogicalName,
                    StringComparer.InvariantCulture
                )
            )
            {
                lines.Add($"    {column.LogicalName}?: {MapTsType(column)};");
            }

            lines.Add("  }");
            lines.Add("}");
            File.WriteAllLines(filePath, lines);

            restRootLines.Add($"    {table.LogicalName}: {safeSchema};");
            internalLines.Add($"    {table.LogicalName}: {safeSchema};");
        }

        restRootLines.Add("  }");
        restRootLines.Add("}");
        internalLines.Add("  }");
        internalLines.Add("}");

        File.WriteAllLines(Path.Combine(outputDirectory, "rest.d.ts"), restRootLines);
        File.WriteAllLines(
            Path.Combine(outputDirectory, "_internal", "rest-entities.d.ts"),
            internalLines
        );
    }

    private static void WriteFormFiles(
        string outputDirectory,
        IReadOnlyList<TableModel> tables,
        ServiceClient service
    )
    {
        var formDir = Path.Combine(outputDirectory, "Form");
        if (Directory.Exists(formDir))
        {
            Directory.Delete(formDir, true);
        }

        Directory.CreateDirectory(formDir);

        var tableMap = tables.ToDictionary(t => t.LogicalName, StringComparer.InvariantCulture);
        var rootEntitySet = new HashSet<string>(StringComparer.InvariantCulture);

        foreach (var table in tables)
        {
            if (!string.IsNullOrWhiteSpace(table.LogicalName))
            {
                rootEntitySet.Add(table.LogicalName);
            }
        }

        var includedForms = ResolveIncludedForms(service, tableMap, rootEntitySet);
        var includedEntities = includedForms
            .Select(f => f.GetAttributeValue<string>("objecttypecode"))
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Cast<string>()
            .ToHashSet(StringComparer.InvariantCulture);

        var missingEntities = includedEntities
            .Where(entity => !tableMap.ContainsKey(entity))
            .Distinct(StringComparer.InvariantCulture)
            .ToList();

        if (missingEntities.Count > 0)
        {
            var fetchConfig = new XrmFetchConfig(
                Solutions: [],
                Entities: missingEntities,
                DeprecatedPrefix: string.Empty,
                LabelMapping: new Dictionary<string, string>(StringComparer.InvariantCulture)
            );

            var fetcher = new DataverseMetadataFetcher(service, fetchConfig);
            var missingTables = fetcher.FetchMetadataAsync().GetAwaiter().GetResult();

            foreach (var missingTable in missingTables)
            {
                if (!tableMap.ContainsKey(missingTable.LogicalName))
                {
                    tableMap[missingTable.LogicalName] = missingTable;
                }
            }
        }

        var attributeTypeHints = BuildAttributeTypeHints(tableMap.Values);
        var bpfControlMap = RetrieveBpfControls(service, includedEntities);

        var formRecords = includedForms
            .Select(form =>
            {
                var entityName = form.GetAttributeValue<string>("objecttypecode") ?? string.Empty;
                var name = form.GetAttributeValue<string>("name") ?? string.Empty;
                var type = form.GetAttributeValue<OptionSetValue>("type")?.Value ?? -1;
                var typeLabel = SanitizeIdentifier(MapFormType(type), "Main");
                var safeName = SanitizeIdentifier(name, "Form");
                var formXml = form.GetAttributeValue<string>("formxml") ?? string.Empty;
                return new
                {
                    FormId = form.Id,
                    EntityName = entityName,
                    FormType = typeLabel,
                    FormName = safeName,
                    FormXml = formXml,
                };
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.EntityName)
                && x.FormType
                    is not "Card"
                        and not "InteractionCentricDashboard"
                        and not "TaskFlowForm"
            )
            .GroupBy(x => (x.EntityName, x.FormType, x.FormName))
            .SelectMany(group =>
                group
                    .OrderBy(x => x.FormId)
                    .Select(
                        (x, index) =>
                            new
                            {
                                x.FormId,
                                x.EntityName,
                                x.FormType,
                                FileName = index == 0 ? x.FormName : $"{x.FormName}{index}",
                                x.FormXml,
                            }
                    )
            )
            .OrderBy(x => x.EntityName, StringComparer.InvariantCulture)
            .ThenBy(x => x.FormType, StringComparer.InvariantCulture)
            .ThenBy(x => x.FileName, StringComparer.InvariantCulture)
            .ToList();

        var formTypeMap = formRecords
            .GroupBy(f => f.FormId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var f = g.First();
                    return (f.EntityName, f.FormType, f.FileName);
                }
            );

        foreach (var form in formRecords)
        {
            var entityDir = Path.Combine(formDir, form.EntityName, form.FormType);
            Directory.CreateDirectory(entityDir);

            tableMap.TryGetValue(form.EntityName, out var table);
            var lines = BuildFormDeclarationLines(
                form.EntityName,
                form.FormType,
                form.FileName,
                form.FormXml,
                table,
                attributeTypeHints,
                formTypeMap,
                bpfControlMap.TryGetValue(form.EntityName, out var bpfControls) ? bpfControls : []
            );

            File.WriteAllLines(Path.Combine(entityDir, $"{form.FileName}.d.ts"), lines);
        }
    }

    private static IReadOnlyList<Entity> ResolveIncludedForms(
        IOrganizationService service,
        IReadOnlyDictionary<string, TableModel> tableMap,
        HashSet<string> rootEntities
    )
    {
        var rootForms = RetrieveForms(service, rootEntities);
        var includedForms = rootForms.ToDictionary(f => f.Id);
        var pendingFormIds = new Queue<Guid>();
        var queuedFormIds = new HashSet<Guid>();

        foreach (var form in rootForms)
        {
            EnqueueEmbeddedReferences(form, tableMap, pendingFormIds, queuedFormIds);
        }

        while (pendingFormIds.Count > 0)
        {
            var batch = new List<Guid>();
            while (pendingFormIds.Count > 0)
            {
                var formId = pendingFormIds.Dequeue();
                if (includedForms.ContainsKey(formId))
                {
                    continue;
                }

                batch.Add(formId);
                if (batch.Count >= 200)
                {
                    break;
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var embeddedForms = RetrieveFormsByIds(service, batch);
            foreach (var form in embeddedForms)
            {
                if (!includedForms.TryAdd(form.Id, form))
                {
                    continue;
                }

                EnqueueEmbeddedReferences(form, tableMap, pendingFormIds, queuedFormIds);
            }
        }

        return includedForms.Values.ToList();
    }

    private static void EnqueueEmbeddedReferences(
        Entity form,
        IReadOnlyDictionary<string, TableModel> tableMap,
        Queue<Guid> pendingFormIds,
        HashSet<Guid> queuedFormIds
    )
    {
        var entityName = form.GetAttributeValue<string>("objecttypecode") ?? string.Empty;
        var formXml = form.GetAttributeValue<string>("formxml") ?? string.Empty;
        tableMap.TryGetValue(entityName, out var table);

        var parsed = ParseFormXml(formXml, table);
        foreach (var quickView in parsed.QuickViewForms)
        {
            if (!quickView.FormId.HasValue)
            {
                continue;
            }

            if (queuedFormIds.Add(quickView.FormId.Value))
            {
                pendingFormIds.Enqueue(quickView.FormId.Value);
            }
        }
    }

    private static List<string> BuildFormDeclarationLines(
        string entityName,
        string formType,
        string formName,
        string formXml,
        TableModel? table,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints,
        IReadOnlyDictionary<
            Guid,
            (string EntityName, string FormType, string FormName)
        > formTypeMap,
        IReadOnlyList<FormControl> bpfControls
    )
    {
        var parsed = ParseFormXml(formXml, table);
        var controls = parsed.Controls.ToList();
        var attributes = parsed.Attributes.ToList();

        if (bpfControls.Count > 0)
        {
            controls.AddRange(bpfControls);

            var attributesByName = attributes.ToDictionary(
                a => a.LogicalName,
                StringComparer.InvariantCultureIgnoreCase
            );
            foreach (var bpf in bpfControls)
            {
                if (string.IsNullOrWhiteSpace(bpf.AttributeName))
                {
                    continue;
                }

                FormControlKind? preferredKind =
                    bpf.Kind == FormControlKind.Default ? null : bpf.Kind;

                if (!attributesByName.TryGetValue(bpf.AttributeName, out var existing))
                {
                    attributesByName[bpf.AttributeName] = new FormAttribute(
                        bpf.AttributeName,
                        true,
                        bpf.TargetEntityUnion,
                        preferredKind
                    );
                    continue;
                }

                attributesByName[bpf.AttributeName] = existing with
                {
                    CanBeNull = existing.CanBeNull,
                    LookupEntityUnion = existing.LookupEntityUnion ?? bpf.TargetEntityUnion,
                    PreferredControlKind = existing.PreferredControlKind ?? preferredKind,
                };
            }

            attributes = attributesByName
                .Values.OrderBy(a => a.LogicalName, StringComparer.InvariantCulture)
                .ToList();
        }

        controls = RenameDuplicateControls(controls)
            .OrderBy(c => c.ControlName, StringComparer.InvariantCulture)
            .ToList();
        attributes = attributes
            .OrderBy(a => a.LogicalName, StringComparer.InvariantCulture)
            .ToList();

        var attributeMap = attributes.ToDictionary(a => a.LogicalName, a => a);

        var lines = new List<string>
        {
            $"declare namespace Form.{entityName}.{formType} {{",
            $"  namespace {formName} {{",
            "    namespace Tabs {",
        };

        foreach (var tab in parsed.Tabs)
        {
            lines.Add($"      interface {tab.SafeName} extends Xrm.SectionCollectionBase {{");
            foreach (var section in tab.Sections)
            {
                lines.Add($"        get(name: \"{section}\"): Xrm.PageSection;");
            }

            lines.Add("        get(name: string): undefined;");
            lines.Add("        get(): Xrm.PageSection[];");
            lines.Add("        get(index: number): Xrm.PageSection;");
            lines.Add(
                "        get(chooser: (item: Xrm.PageSection, index: number) => boolean): Xrm.PageSection[];"
            );
            lines.Add("      }");
        }

        lines.Add("    }");
        lines.Add("    interface Attributes extends Xrm.AttributeCollectionBase {");

        foreach (var attribute in attributes)
        {
            var attributeType = ResolveAttributeType(table, attribute, attributeTypeHints);
            lines.Add($"      get(name: \"{attribute.LogicalName}\"): {attributeType};");
        }

        lines.Add("      get(name: string): undefined;");
        lines.Add("      get(): Xrm.Attribute<any>[];");
        lines.Add("      get(index: number): Xrm.Attribute<any>;");
        lines.Add(
            "      get(chooser: (item: Xrm.Attribute<any>, index: number) => boolean): Xrm.Attribute<any>[];"
        );
        lines.Add("    }");

        lines.Add("    interface Controls extends Xrm.ControlCollectionBase {");
        foreach (var control in controls.Where(c => c.Kind != FormControlKind.QuickView))
        {
            var attr =
                !string.IsNullOrWhiteSpace(control.AttributeName)
                && attributeMap.TryGetValue(control.AttributeName, out var found)
                    ? found
                    : null;
            var controlType = ResolveControlType(table, control, attr, attributeTypeHints);
            lines.Add($"      get(name: \"{control.ControlName}\"): {controlType};");
        }

        lines.Add("      get(name: string): undefined;");
        lines.Add("      get(): Xrm.BaseControl[];");
        lines.Add("      get(index: number): Xrm.BaseControl;");
        lines.Add(
            "      get(chooser: (item: Xrm.BaseControl, index: number) => boolean): Xrm.BaseControl[];"
        );
        lines.Add("    }");

        if (parsed.QuickViewForms.Count > 0)
        {
            lines.Add("    interface QuickViewForms extends Xrm.QuickViewFormsBase {");
            foreach (var quickView in parsed.QuickViewForms)
            {
                var returnType = "Xrm.QuickViewFormBase";
                if (
                    quickView.FormId.HasValue
                    && formTypeMap.TryGetValue(quickView.FormId.Value, out var targetForm)
                )
                {
                    returnType =
                        $"Form.{targetForm.EntityName}.{targetForm.FormType}.{targetForm.FormName}";
                }

                lines.Add($"      get(name: \"{quickView.ControlName}\"): {returnType};");
            }

            lines.Add("    }");
        }

        lines.Add("    interface Tabs extends Xrm.TabCollectionBase {");
        foreach (var tab in parsed.Tabs)
        {
            lines.Add($"      get(name: \"{tab.Name}\"): Xrm.PageTab<Tabs.{tab.SafeName}>;");
        }

        lines.Add("      get(name: string): undefined;");
        lines.Add("      get(): Xrm.PageTab<Xrm.Collection<Xrm.PageSection>>[];");
        lines.Add("      get(index: number): Xrm.PageTab<Xrm.Collection<Xrm.PageSection>>;");
        lines.Add(
            "      get(chooser: (item: Xrm.PageTab<Xrm.Collection<Xrm.PageSection>>, index: number) => boolean): Xrm.PageTab<Xrm.Collection<Xrm.PageSection>>[];"
        );
        lines.Add("    }");
        lines.Add("  }");

        var quickViewFormsType =
            parsed.QuickViewForms.Count > 0
                ? $"{formName}.QuickViewForms"
                : "Xrm.QuickViewFormsBase";
        var baseInterface = string.Equals(formType, "Quick", StringComparison.InvariantCulture)
            ? $"Xrm.QuickViewForm<{formName}.Tabs,{formName}.Controls>"
            : $"Xrm.PageBase<{formName}.Attributes,{formName}.Tabs,{formName}.Controls,{quickViewFormsType}>";
        lines.Add($"  interface {formName} extends {baseInterface} {{");

        foreach (var attribute in attributes)
        {
            var attributeType = ResolveAttributeType(table, attribute, attributeTypeHints);
            lines.Add(
                $"    getAttribute(attributeName: \"{attribute.LogicalName}\"): {attributeType};"
            );
        }

        lines.Add("    getAttribute(attributeName: string): undefined;");
        lines.Add(
            "    getAttribute(delegateFunction: Xrm.Collection.MatchingDelegate<Xrm.Attribute<any>>): Xrm.Attribute<any>[];"
        );

        foreach (var control in controls.Where(c => c.Kind != FormControlKind.QuickView))
        {
            var attr =
                !string.IsNullOrWhiteSpace(control.AttributeName)
                && attributeMap.TryGetValue(control.AttributeName, out var found)
                    ? found
                    : null;
            var controlType = ResolveControlType(table, control, attr, attributeTypeHints);
            lines.Add($"    getControl(controlName: \"{control.ControlName}\"): {controlType};");
        }

        lines.Add("    getControl(controlName: string): undefined;");
        lines.Add(
            "    getControl(delegateFunction: Xrm.Collection.MatchingDelegate<Xrm.Control<any>>): Xrm.Control<any>[];"
        );
        lines.Add("  }");
        lines.Add("}");

        return lines;
    }

    private static string ResolveAttributeType(
        TableModel? table,
        FormAttribute attribute,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        var column = FindColumn(table, attribute.LogicalName, attributeTypeHints);

        if (column == null)
        {
            return InferAttributeTypeFromControlKind(attribute);
        }

        var metadataType = ToLegacyAttributeType(
            table
                ?? new TableModel
                {
                    LogicalName = string.Empty,
                    SchemaName = string.Empty,
                    DisplayName = string.Empty,
                    Description = string.Empty,
                    Columns = [],
                    Relationships = [],
                    Keys = [],
                },
            column,
            attribute
        );
        if (
            metadataType.StartsWith("Xrm.Attribute<any>", StringComparison.InvariantCulture)
            && attribute.PreferredControlKind.HasValue
            && attribute.PreferredControlKind.Value != FormControlKind.Default
        )
        {
            var inferredType = InferAttributeTypeFromControlKind(attribute);
            if (!inferredType.StartsWith("Xrm.Attribute<any>", StringComparison.InvariantCulture))
            {
                return inferredType;
            }
        }

        return metadataType;
    }

    private static string InferAttributeTypeFromControlKind(FormAttribute attribute)
    {
        var returnType = attribute.PreferredControlKind switch
        {
            FormControlKind.OptionSet => "Xrm.OptionSetAttribute<number>",
            FormControlKind.MultiSelectOptionSet => "Xrm.MultiSelectOptionSetAttribute<number>",
            FormControlKind.Lookup =>
                $"Xrm.LookupAttribute<{attribute.LookupEntityUnion ?? "string"}>",
            FormControlKind.Date => "Xrm.DateAttribute",
            FormControlKind.Number => "Xrm.NumberAttribute",
            FormControlKind.String => "Xrm.Attribute<string>",
            _ => "Xrm.Attribute<any>",
        };

        return attribute.CanBeNull ? $"{returnType} | null" : returnType;
    }

    private static string ResolveControlType(
        TableModel? table,
        FormControl control,
        FormAttribute? attribute,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        string? hintLookupUnion = null;
        if (
            !string.IsNullOrWhiteSpace(control.AttributeName)
            && attributeTypeHints.TryGetValue(control.AttributeName, out var hintedColumn)
        )
        {
            hintLookupUnion = LookupUnionFromColumn(hintedColumn);
        }

        var lookupTargets = ResolveLookupUnion(
            table,
            control.AttributeName,
            control.TargetEntityUnion,
            attribute?.LookupEntityUnion,
            hintLookupUnion
        );

        var returnType = control.Kind switch
        {
            FormControlKind.Lookup => $"Xrm.LookupControl<{lookupTargets}>",
            FormControlKind.SubGrid => $"Xrm.SubGridControl<{lookupTargets}>",
            FormControlKind.Date => "Xrm.DateControl",
            FormControlKind.OptionSet => attribute != null && table != null
                ? ResolveOptionSetControlFromAttribute(table, attribute, attributeTypeHints)
                : "Xrm.OptionSetControl<number>",
            FormControlKind.MultiSelectOptionSet => attribute != null && table != null
                ? ResolveMultiSelectControlFromAttribute(table, attribute, attributeTypeHints)
                : "Xrm.MultiSelectOptionSetControl<number>",
            FormControlKind.Number => "Xrm.NumberControl",
            FormControlKind.String => "Xrm.StringControl",
            FormControlKind.WebResource
            or FormControlKind.IFrame
            or FormControlKind.KnowledgeBaseSearch => "Xrm.BaseControl",
            _ => ResolveFallbackControlType(
                table,
                control.AttributeName,
                attribute,
                attributeTypeHints
            ),
        };

        return IsBpfControlName(control.ControlName) ? $"{returnType} | null" : returnType;
    }

    private static string ResolveFallbackControlType(
        TableModel? table,
        string? attributeName,
        FormAttribute? attribute,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return "Xrm.BaseControl";
        }

        var column = FindColumn(table, attributeName, attributeTypeHints);

        if (column == null)
        {
            return "Xrm.BaseControl";
        }

        return column switch
        {
            LookupColumnModel =>
                $"Xrm.LookupControl<{ResolveLookupUnion(table, attributeName, attribute?.LookupEntityUnion, LookupUnionFromColumn(column))}>",
            PartyListColumnModel =>
                $"Xrm.LookupControl<{ResolveLookupUnion(table, attributeName, attribute?.LookupEntityUnion)}>",
            DateTimeColumnModel => "Xrm.DateControl",
            EnumColumnModel enumColumn when enumColumn.IsMultiSelect =>
                $"Xrm.MultiSelectOptionSetControl<{GetOptionSetTypeName(enumColumn)}>",
            EnumColumnModel enumColumn =>
                $"Xrm.OptionSetControl<{GetOptionSetTypeName(enumColumn)}>",
            IntegerColumnModel
            or BigIntColumnModel
            or DecimalColumnModel
            or DoubleColumnModel
            or MoneyColumnModel => "Xrm.NumberControl",
            BooleanColumnModel or BooleanManagedColumnModel => "Xrm.OptionSetControl<boolean>",
            StringColumnModel or MemoColumnModel => "Xrm.StringControl",
            _ => "Xrm.Control<Xrm.Attribute<any>>",
        };
    }

    private static string ResolveOptionSetControlFromAttribute(
        TableModel table,
        FormAttribute attribute,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        var column = FindColumn(table, attribute.LogicalName, attributeTypeHints);

        return column switch
        {
            EnumColumnModel enumColumn =>
                $"Xrm.OptionSetControl<{GetOptionSetTypeName(enumColumn)}>",
            BooleanColumnModel or BooleanManagedColumnModel => "Xrm.OptionSetControl<boolean>",
            _ => "Xrm.OptionSetControl<number>",
        };
    }

    private static string ResolveMultiSelectControlFromAttribute(
        TableModel table,
        FormAttribute attribute,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        var column = FindColumn(table, attribute.LogicalName, attributeTypeHints);

        return column switch
        {
            EnumColumnModel enumColumn when enumColumn.IsMultiSelect =>
                $"Xrm.MultiSelectOptionSetControl<{GetOptionSetTypeName(enumColumn)}>",
            EnumColumnModel enumColumn =>
                $"Xrm.OptionSetControl<{GetOptionSetTypeName(enumColumn)}>",
            BooleanColumnModel or BooleanManagedColumnModel => "Xrm.OptionSetControl<boolean>",
            _ => "Xrm.MultiSelectOptionSetControl<number>",
        };
    }

    private static string ToLegacyAttributeType(
        TableModel table,
        ColumnModel column,
        FormAttribute attribute
    )
    {
        var returnType = column switch
        {
            LookupColumnModel =>
                $"Xrm.LookupAttribute<{ResolveLookupUnion(table, attribute.LogicalName, attribute.LookupEntityUnion, LookupUnionFromColumn(column))}>",
            PartyListColumnModel =>
                $"Xrm.LookupAttribute<{ResolveLookupUnion(table, attribute.LogicalName, attribute.LookupEntityUnion, LookupUnionFromColumn(column))}>",
            DateTimeColumnModel => "Xrm.DateAttribute",
            EnumColumnModel enumColumn when enumColumn.IsMultiSelect =>
                $"Xrm.MultiSelectOptionSetAttribute<{GetOptionSetTypeName(enumColumn)}>",
            EnumColumnModel enumColumn =>
                $"Xrm.OptionSetAttribute<{GetOptionSetTypeName(enumColumn)}>",
            IntegerColumnModel
            or BigIntColumnModel
            or DecimalColumnModel
            or DoubleColumnModel
            or MoneyColumnModel => "Xrm.NumberAttribute",
            BooleanColumnModel or BooleanManagedColumnModel => "Xrm.OptionSetAttribute<boolean>",
            StringColumnModel or MemoColumnModel => "Xrm.Attribute<string>",
            _ => "Xrm.Attribute<any>",
        };

        return attribute.CanBeNull ? $"{returnType} | null" : returnType;
    }

    private static ColumnModel? FindColumn(
        TableModel? table,
        string attributeLogicalName,
        IReadOnlyDictionary<string, ColumnModel> attributeTypeHints
    )
    {
        if (table != null)
        {
            var tableColumn = table.Columns.FirstOrDefault(c =>
                string.Equals(
                    c.LogicalName,
                    attributeLogicalName,
                    StringComparison.InvariantCulture
                )
            );

            if (tableColumn != null)
            {
                return tableColumn;
            }
        }

        if (attributeTypeHints.TryGetValue(attributeLogicalName, out var hintedColumn))
        {
            return hintedColumn;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, ColumnModel> BuildAttributeTypeHints(
        IEnumerable<TableModel> tables
    )
    {
        var map = new Dictionary<string, ColumnModel>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var column in tables.SelectMany(t => t.Columns))
        {
            if (!map.TryGetValue(column.LogicalName, out var existing))
            {
                map[column.LogicalName] = column;
                continue;
            }

            map[column.LogicalName] = PreferHint(existing, column);
        }

        return map;
    }

    private static ColumnModel PreferHint(ColumnModel current, ColumnModel candidate)
    {
        var currentScore = GetHintScore(current);
        var candidateScore = GetHintScore(candidate);
        return candidateScore > currentScore ? candidate : current;
    }

    private static int GetHintScore(ColumnModel column)
    {
        return column switch
        {
            LookupColumnModel => 100,
            PartyListColumnModel => 95,
            BooleanColumnModel or BooleanManagedColumnModel => 90,
            EnumColumnModel => 80,
            DateTimeColumnModel => 70,
            IntegerColumnModel
            or BigIntColumnModel
            or DecimalColumnModel
            or DoubleColumnModel
            or MoneyColumnModel => 60,
            StringColumnModel or MemoColumnModel => 50,
            _ => 0,
        };
    }

    private static bool IsBpfControlName(string? controlName)
    {
        return !string.IsNullOrWhiteSpace(controlName)
            && controlName.StartsWith(
                "header_process_",
                StringComparison.InvariantCultureIgnoreCase
            );
    }

    private static string LookupUnionFromColumn(ColumnModel column)
    {
        IReadOnlyList<string>? targetTables = column switch
        {
            LookupColumnModel lookup => lookup.TargetTables,
            PartyListColumnModel partyList => partyList.TargetTables,
            _ => null,
        };

        if (targetTables == null)
        {
            return "string";
        }

        var targets = targetTables
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.InvariantCulture)
            .Select(t => $"\"{t}\"")
            .ToList();

        if (targets.Count > 0)
        {
            return string.Join(" | ", targets);
        }

        return
            column is LookupColumnModel singleLookup
            && !string.IsNullOrWhiteSpace(singleLookup.TargetTable)
            ? $"\"{singleLookup.TargetTable}\""
            : "string";
    }

    private static string ResolveLookupUnion(
        TableModel? table,
        string? attributeLogicalName,
        params string?[] candidateUnions
    )
    {
        var values = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var candidate in candidateUnions)
        {
            AddLookupUnionValues(values, candidate);
        }

        if (table != null && !string.IsNullOrWhiteSpace(attributeLogicalName))
        {
            AddLookupUnionValues(values, GetLookupUnion(table, attributeLogicalName));
        }

        if (values.Count == 0)
        {
            return "string";
        }

        return string.Join(
            " | ",
            values.OrderBy(v => v, StringComparer.InvariantCulture).Select(v => $"\"{v}\"")
        );
    }

    private static void AddLookupUnionValues(ISet<string> values, string? union)
    {
        if (string.IsNullOrWhiteSpace(union))
        {
            return;
        }

        var items = union.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            var normalized = item.Trim().Trim('"').Trim();
            if (
                string.IsNullOrWhiteSpace(normalized)
                || string.Equals(normalized, "string", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                continue;
            }

            values.Add(normalized);
        }
    }

    private static string GetLookupUnion(TableModel table, string attributeLogicalName)
    {
        if (
            string.Equals(
                attributeLogicalName,
                "ownerid",
                StringComparison.InvariantCultureIgnoreCase
            )
        )
        {
            return "\"systemuser\" | \"team\"";
        }

        var relationshipTargets = table
            .Relationships.Where(r =>
                !string.IsNullOrWhiteSpace(r.ThisEntityAttribute)
                && string.Equals(
                    r.ThisEntityAttribute,
                    attributeLogicalName,
                    StringComparison.InvariantCulture
                )
                && !string.IsNullOrWhiteSpace(r.RelatedEntity)
            )
            .Select(r => r.RelatedEntity!)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .Select(e => $"\"{e}\"")
            .ToList();

        var column = table.Columns.FirstOrDefault(c =>
            string.Equals(c.LogicalName, attributeLogicalName, StringComparison.InvariantCulture)
        );

        var resolved = ResolveLookupUnion(
            null,
            null,
            string.Join(" | ", relationshipTargets),
            column != null ? LookupUnionFromColumn(column) : null
        );

        return resolved;
    }

    private static string GetOptionSetTypeName(EnumColumnModel enumColumn)
    {
        return SanitizeIdentifier(enumColumn.OptionsetName, "number");
    }

    private static ParsedForm ParseFormXml(string formXml, TableModel? table)
    {
        if (string.IsNullOrWhiteSpace(formXml))
        {
            return new ParsedForm([], [], [], []);
        }

        try
        {
            var doc = XDocument.Parse(formXml, LoadOptions.None);

            var tabs = doc.Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "tab",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(tab =>
                {
                    var tabName = SanitizeIdentifier(
                        tab.Attribute("name")?.Value ?? tab.Attribute("id")?.Value ?? "Tab",
                        "Tab"
                    );

                    var sections = tab.Descendants()
                        .Where(e =>
                            string.Equals(
                                e.Name.LocalName,
                                "section",
                                StringComparison.InvariantCultureIgnoreCase
                            )
                        )
                        .Select(section =>
                            SanitizeIdentifier(
                                section.Attribute("name")?.Value
                                    ?? section.Attribute("id")?.Value
                                    ?? "Section",
                                "Section"
                            )
                        )
                        .Distinct(StringComparer.InvariantCulture)
                        .OrderBy(x => x, StringComparer.InvariantCulture)
                        .ToList();

                    return new FormTab(tabName, SanitizeIdentifier(tabName, "Tab"), sections);
                })
                .DistinctBy(t => t.Name)
                .OrderBy(t => t.Name, StringComparer.InvariantCulture)
                .ToList();

            var controlDescriptions = GetControlDescriptions(doc);

            var controls = doc.Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "control",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(control =>
                {
                    var name = control.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = control.Attribute("datafieldname")?.Value;
                    }

                    var attributeName = control.Attribute("datafieldname")?.Value;
                    var relationshipName = control.Attribute("relationship")?.Value;
                    var classId = NormalizeGuidText(control.Attribute("classid")?.Value);
                    var uniqueId = control.Attribute("uniqueid")?.Value;
                    var kind = ResolveControlKind(name, classId, uniqueId, controlDescriptions);
                    if (
                        kind == FormControlKind.Default
                        && string.IsNullOrWhiteSpace(attributeName)
                        && !string.IsNullOrWhiteSpace(relationshipName)
                    )
                    {
                        kind = FormControlKind.SubGrid;
                    }
                    var targetEntityUnion = GetTargetEntityUnion(control, table);
                    var quickView = GetQuickViewReference(control);

                    var canBeNull =
                        attributeName != null
                        && (
                            Regex.IsMatch(
                                attributeName,
                                "^address\\d_composite$",
                                RegexOptions.IgnoreCase
                            )
                            || string.Equals(
                                attributeName,
                                "fullname",
                                StringComparison.InvariantCultureIgnoreCase
                            )
                        );

                    return new FormControl(
                        SanitizeIdentifier(name ?? "Control", "Control"),
                        attributeName,
                        classId,
                        kind,
                        targetEntityUnion,
                        canBeNull,
                        quickView
                    );
                })
                .ToList();

            var existingControlNames = new HashSet<string>(
                controls.Select(c => c.ControlName),
                StringComparer.InvariantCultureIgnoreCase
            );

            var descriptorControls = doc.Descendants()
                .Attributes()
                .Where(a =>
                    string.Equals(
                        a.Name.LocalName,
                        "forControl",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(a => a.Value?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Where(v =>
                    v.StartsWith("header_process_", StringComparison.InvariantCultureIgnoreCase)
                    || v.StartsWith("header_", StringComparison.InvariantCultureIgnoreCase)
                )
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .Where(v => !existingControlNames.Contains(v))
                .Select(controlName =>
                {
                    var attributeName = DeriveHeaderAttributeName(controlName);
                    var targetEntityUnion = GetTargetEntityUnionForControl(doc, controlName);
                    var kind = !string.IsNullOrWhiteSpace(attributeName)
                        ? FormControlKind.Default
                        : ResolveControlKind(controlName, null);

                    return new FormControl(
                        SanitizeIdentifier(controlName, "Control"),
                        attributeName,
                        null,
                        kind,
                        targetEntityUnion,
                        controlName.StartsWith(
                            "header_process_",
                            StringComparison.InvariantCultureIgnoreCase
                        ),
                        null
                    );
                })
                .ToList();

            controls.AddRange(descriptorControls);

            existingControlNames = new HashSet<string>(
                controls.Select(c => c.ControlName),
                StringComparer.InvariantCultureIgnoreCase
            );

            var idBasedHeaderControls = doc.Descendants()
                .SelectMany(e =>
                    e.Attributes()
                        .Where(a =>
                            string.Equals(
                                a.Name.LocalName,
                                "id",
                                StringComparison.InvariantCultureIgnoreCase
                            )
                        )
                        .Select(a => (Element: e, ControlName: a.Value?.Trim()))
                )
                .Where(x => !string.IsNullOrWhiteSpace(x.ControlName))
                .Select(x => (x.Element, ControlName: x.ControlName!))
                .Where(x =>
                    x.ControlName.StartsWith(
                        "header_process_",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                    || x.ControlName.StartsWith(
                        "header_",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Where(x => !existingControlNames.Contains(x.ControlName))
                .Select(x =>
                {
                    var attributeName = DeriveHeaderAttributeName(x.ControlName);
                    var targetEntityUnion = GetTargetEntityUnionFromElement(x.Element);

                    return new FormControl(
                        SanitizeIdentifier(x.ControlName, "Control"),
                        attributeName,
                        null,
                        FormControlKind.Default,
                        targetEntityUnion,
                        x.ControlName.StartsWith(
                            "header_process_",
                            StringComparison.InvariantCultureIgnoreCase
                        ),
                        null
                    );
                })
                .ToList();

            controls.AddRange(idBasedHeaderControls);

            existingControlNames = new HashSet<string>(
                controls.Select(c => c.ControlName),
                StringComparer.InvariantCultureIgnoreCase
            );

            var regexHeaderControls = Regex
                .Matches(
                    formXml,
                    "header_process_[A-Za-z0-9_]+|header_[A-Za-z0-9_]+",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                )
                .Select(m => m.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .Where(v => !existingControlNames.Contains(v))
                .Select(controlName => new FormControl(
                    SanitizeIdentifier(controlName, "Control"),
                    DeriveHeaderAttributeName(controlName),
                    null,
                    FormControlKind.Default,
                    null,
                    controlName.StartsWith(
                        "header_process_",
                        StringComparison.InvariantCultureIgnoreCase
                    ),
                    null
                ))
                .ToList();

            controls.AddRange(regexHeaderControls);

            var compositeControls = controls
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.AttributeName)
                    && Regex.IsMatch(
                        c.AttributeName,
                        "^address(\\d)_composite$",
                        RegexOptions.IgnoreCase
                    )
                )
                .SelectMany(c =>
                {
                    var match = Regex.Match(
                        c.AttributeName!,
                        "^address(\\d)_composite$",
                        RegexOptions.IgnoreCase
                    );
                    var idx = match.Groups[1].Value;
                    var fields = new[]
                    {
                        $"address{idx}_line1",
                        $"address{idx}_line2",
                        $"address{idx}_line3",
                        $"address{idx}_city",
                        $"address{idx}_stateorprovince",
                        $"address{idx}_postalcode",
                        $"address{idx}_country",
                    };

                    return fields.Select(field => new FormControl(
                        SanitizeIdentifier(
                            $"{c.ControlName}_compositionLinkControl_{field}",
                            "Control"
                        ),
                        field,
                        null,
                        FormControlKind.String,
                        null,
                        true,
                        null
                    ));
                })
                .ToList();

            var normalizedControls = RenameDuplicateControls(
                    controls.Concat(compositeControls).ToList()
                )
                .OrderBy(c => c.ControlName, StringComparer.InvariantCulture)
                .ToList();

            var attributes = normalizedControls
                .Select(c => c.AttributeName)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
                .GroupBy(a => a, StringComparer.InvariantCulture)
                .Select(g =>
                {
                    var related = normalizedControls
                        .Where(c =>
                            string.Equals(c.AttributeName, g.Key, StringComparison.InvariantCulture)
                        )
                        .ToList();
                    var lookupUnion = ResolveLookupUnion(
                        null,
                        null,
                        related.Select(c => c.TargetEntityUnion).ToArray()
                    );
                    var canBeNull = related.All(c => IsBpfControlName(c.ControlName));
                    var preferredKind = related
                        .Select(r => r.Kind)
                        .FirstOrDefault(k =>
                            k
                                is FormControlKind.Lookup
                                    or FormControlKind.OptionSet
                                    or FormControlKind.MultiSelectOptionSet
                                    or FormControlKind.Date
                                    or FormControlKind.Number
                                    or FormControlKind.String
                        );

                    return new FormAttribute(
                        g.Key,
                        canBeNull,
                        string.Equals(lookupUnion, "string", StringComparison.InvariantCulture)
                            ? null
                            : lookupUnion,
                        preferredKind == FormControlKind.Default ? null : preferredKind
                    );
                })
                .OrderBy(a => a.LogicalName, StringComparer.InvariantCulture)
                .ToList();

            var quickViewForms = normalizedControls
                .Where(c => c.Kind == FormControlKind.QuickView && c.QuickViewReference != null)
                .Select(c => new QuickViewFormReference(
                    c.ControlName,
                    c.QuickViewReference!.Value.EntityName,
                    c.QuickViewReference.Value.FormId
                ))
                .DistinctBy(q => q.ControlName)
                .OrderBy(q => q.ControlName, StringComparer.InvariantCulture)
                .ToList();

            return new ParsedForm(tabs, normalizedControls, attributes, quickViewForms);
        }
        catch
        {
            return new ParsedForm([], [], [], []);
        }
    }

    private static List<FormControl> RenameDuplicateControls(List<FormControl> controls)
    {
        return controls
            .GroupBy(c => c.ControlName, StringComparer.InvariantCulture)
            .SelectMany(group =>
                group.Select(
                    (control, index) =>
                        index == 0
                            ? control
                            : control with
                            {
                                ControlName = control.ControlName.StartsWith(
                                    "header_process_",
                                    StringComparison.InvariantCultureIgnoreCase
                                )
                                    ? $"{control.ControlName}_{index}"
                                    : $"{control.ControlName}{index}",
                            }
                )
            )
            .ToList();
    }

    private static FormControlKind ResolveControlKind(string? id, string? classId)
    {
        return ResolveControlKind(id, classId, null, null);
    }

    private static FormControlKind ResolveControlKind(
        string? id,
        string? classId,
        string? uniqueId,
        IReadOnlyDictionary<string, string>? controlDescriptions
    )
    {
        if (
            !string.IsNullOrWhiteSpace(id)
            && id.StartsWith("WebResource_", StringComparison.InvariantCultureIgnoreCase)
        )
        {
            return FormControlKind.WebResource;
        }

        var normalizedClassId = NormalizeGuidText(classId);
        if (
            string.Equals(
                normalizedClassId,
                "F9A8A302-114E-466A-B582-6771B2AE0D92",
                StringComparison.InvariantCultureIgnoreCase
            )
            && !string.IsNullOrWhiteSpace(uniqueId)
            && controlDescriptions != null
        )
        {
            if (controlDescriptions.TryGetValue(uniqueId, out var customControlId))
            {
                normalizedClassId = NormalizeGuidText(customControlId);
            }
            else if (
                controlDescriptions.TryGetValue(
                    NormalizeControlDescriptionKey(uniqueId),
                    out customControlId
                )
            )
            {
                normalizedClassId = NormalizeGuidText(customControlId);
            }
            else if (
                !string.IsNullOrWhiteSpace(id)
                && controlDescriptions.TryGetValue(id, out customControlId)
            )
            {
                normalizedClassId = NormalizeGuidText(customControlId);
            }
            else if (
                !string.IsNullOrWhiteSpace(id)
                && controlDescriptions.TryGetValue(
                    NormalizeControlDescriptionKey(id),
                    out customControlId
                )
            )
            {
                normalizedClassId = NormalizeGuidText(customControlId);
            }
        }

        if (
            !string.IsNullOrWhiteSpace(normalizedClassId)
            && ControlClassMap.TryGetValue(normalizedClassId, out var kind)
        )
        {
            return kind;
        }

        if (
            !string.IsNullOrWhiteSpace(id)
            && id.Contains("subgrid", StringComparison.InvariantCultureIgnoreCase)
        )
        {
            return FormControlKind.SubGrid;
        }

        return FormControlKind.Default;
    }

    private static Dictionary<string, string> GetControlDescriptions(XDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (
            var description in doc.Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "controlDescription",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
        )
        {
            var forControl = description.Attribute("forControl")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(forControl))
            {
                continue;
            }

            var customControlId = description
                .Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "customControl",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(e => e.Attribute("id")?.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (string.IsNullOrWhiteSpace(customControlId))
            {
                continue;
            }

            if (!map.ContainsKey(forControl))
            {
                map[forControl] = customControlId;
            }

            var normalized = NormalizeControlDescriptionKey(forControl);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = customControlId;
            }
        }

        return map;
    }

    private static string NormalizeControlDescriptionKey(string value)
    {
        return Regex.Replace(value.Trim().ToUpperInvariant(), "[{}]", string.Empty);
    }

    private static string? GetTargetEntityUnion(XElement control, TableModel? table)
    {
        var entities = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (
            var raw in control
                .Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "TargetEntityType",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(e => e.Value)
        )
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var split = raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in split)
            {
                var value = item.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    entities.Add(value);
                }
            }
        }

        if (entities.Count == 0 && table != null)
        {
            var relationshipName = control.Attribute("relationship")?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipName))
            {
                foreach (var related in table.Relationships)
                {
                    if (
                        string.Equals(
                            related.SchemaName,
                            relationshipName,
                            StringComparison.InvariantCultureIgnoreCase
                        ) && !string.IsNullOrWhiteSpace(related.RelatedEntity)
                    )
                    {
                        entities.Add(related.RelatedEntity!);
                    }
                }
            }
        }

        if (entities.Count == 0)
        {
            return null;
        }

        return string.Join(
            " | ",
            entities.OrderBy(v => v, StringComparer.InvariantCulture).Select(v => $"\"{v}\"")
        );
    }

    private static string? GetTargetEntityUnionForControl(XDocument formXml, string controlName)
    {
        var entities = formXml
            .Descendants()
            .Where(e =>
                e.Attributes()
                    .Any(a =>
                        string.Equals(
                            a.Name.LocalName,
                            "forControl",
                            StringComparison.InvariantCultureIgnoreCase
                        )
                        && string.Equals(
                            a.Value,
                            controlName,
                            StringComparison.InvariantCultureIgnoreCase
                        )
                    )
            )
            .SelectMany(GetTargetEntityTypesFromElement)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.InvariantCulture)
            .ToList();

        if (entities.Count == 0)
        {
            return null;
        }

        return string.Join(" | ", entities.Select(v => $"\"{v}\""));
    }

    private static string? GetTargetEntityUnionFromElement(XElement element)
    {
        var entities = GetTargetEntityTypesFromElement(element)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.InvariantCulture)
            .ToList();

        if (entities.Count == 0)
        {
            return null;
        }

        return string.Join(" | ", entities.Select(v => $"\"{v}\""));
    }

    private static IEnumerable<string> GetTargetEntityTypesFromElement(XElement element)
    {
        foreach (
            var raw in element
                .Descendants()
                .Where(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "TargetEntityType",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                .Select(e => e.Value)
        )
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var split = raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in split)
            {
                var value = item.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static string? DeriveHeaderAttributeName(string controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
        {
            return null;
        }

        var rawName = controlName;
        if (rawName.StartsWith("header_process_", StringComparison.InvariantCultureIgnoreCase))
        {
            rawName = rawName["header_process_".Length..];
        }
        else if (rawName.StartsWith("header_", StringComparison.InvariantCultureIgnoreCase))
        {
            rawName = rawName["header_".Length..];
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var withoutNumericSuffix = Regex.Replace(
            rawName,
            "_\\d+$",
            string.Empty,
            RegexOptions.CultureInvariant
        );

        return string.IsNullOrWhiteSpace(withoutNumericSuffix) ? null : withoutNumericSuffix;
    }

    private static (string EntityName, Guid FormId)? GetQuickViewReference(XElement control)
    {
        var quickFormsXml = control
            .Descendants()
            .Where(e =>
                string.Equals(
                    e.Name.LocalName,
                    "QuickForms",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            .Select(e => e.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (string.IsNullOrWhiteSpace(quickFormsXml))
        {
            return null;
        }

        try
        {
            var xdoc = XElement.Parse(quickFormsXml);
            var qfId = xdoc.Descendants()
                .FirstOrDefault(e =>
                    string.Equals(
                        e.Name.LocalName,
                        "QuickFormId",
                        StringComparison.InvariantCultureIgnoreCase
                    )
                );
            if (qfId == null || !Guid.TryParse(qfId.Value, out var formId))
            {
                return null;
            }

            var entityName = qfId.Attribute("entityname")?.Value;
            if (string.IsNullOrWhiteSpace(entityName))
            {
                return null;
            }

            return (entityName, formId);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeGuidText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Regex.Replace(value.ToUpperInvariant(), "[{}]", string.Empty);
    }

    private sealed record ParsedForm(
        IReadOnlyList<FormTab> Tabs,
        IReadOnlyList<FormControl> Controls,
        IReadOnlyList<FormAttribute> Attributes,
        IReadOnlyList<QuickViewFormReference> QuickViewForms
    );

    private sealed record FormTab(string Name, string SafeName, IReadOnlyList<string> Sections);

    private sealed record FormControl(
        string ControlName,
        string? AttributeName,
        string? ClassId,
        FormControlKind Kind,
        string? TargetEntityUnion,
        bool CanBeNull,
        (string EntityName, Guid FormId)? QuickViewReference
    );

    private sealed record FormAttribute(
        string LogicalName,
        bool CanBeNull,
        string? LookupEntityUnion,
        FormControlKind? PreferredControlKind
    );

    private sealed record QuickViewFormReference(
        string ControlName,
        string EntityName,
        Guid? FormId
    );

    private enum FormControlKind
    {
        Default,
        Number,
        Date,
        Lookup,
        OptionSet,
        MultiSelectOptionSet,
        SubGrid,
        WebResource,
        IFrame,
        QuickView,
        KnowledgeBaseSearch,
        String,
    }

    private static List<Entity> RetrieveForms(
        IOrganizationService service,
        HashSet<string> entities
    )
    {
        if (entities.Count == 0)
        {
            return [];
        }

        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("name", "type", "objecttypecode", "formxml"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(
                        "objecttypecode",
                        ConditionOperator.In,
                        entities.Cast<object>().ToArray()
                    ),
                    new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                },
            },
        };

        return service.RetrieveMultiple(query).Entities.ToList();
    }

    private static List<Entity> RetrieveFormsByIds(
        IOrganizationService service,
        IReadOnlyCollection<Guid> formIds
    )
    {
        if (formIds.Count == 0)
        {
            return [];
        }

        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("name", "type", "objecttypecode", "formxml"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(
                        "formid",
                        ConditionOperator.In,
                        formIds.Cast<object>().ToArray()
                    ),
                    new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                },
            },
        };

        return service.RetrieveMultiple(query).Entities.ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FormControl>> RetrieveBpfControls(
        IOrganizationService service,
        HashSet<string> entities
    )
    {
        var query = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("clientdata", "category"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("category", ConditionOperator.Equal, 4),
                    new ConditionExpression("clientdata", ConditionOperator.NotNull),
                },
            },
        };

        var result = new Dictionary<string, List<FormControl>>(StringComparer.InvariantCulture);

        foreach (var workflow in service.RetrieveMultiple(query).Entities)
        {
            var clientData = workflow.GetAttributeValue<string>("clientdata");

            if (string.IsNullOrWhiteSpace(clientData))
            {
                continue;
            }

            var parsedByEntity = ParseBpfControls(clientData);
            foreach (var (entity, entityControls) in parsedByEntity)
            {
                if (string.IsNullOrWhiteSpace(entity) || !entities.Contains(entity))
                {
                    continue;
                }

                if (!result.TryGetValue(entity, out var controls))
                {
                    controls = [];
                    result[entity] = controls;
                }

                controls.AddRange(entityControls);
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp =>
                (IReadOnlyList<FormControl>)
                    kvp.Value.OrderBy(c => c.ControlName, StringComparer.InvariantCulture).ToList(),
            StringComparer.InvariantCulture
        );
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FormControl>> ParseBpfControls(
        string clientData
    )
    {
        try
        {
            using var doc = JsonDocument.Parse(clientData);
            var byEntity = new Dictionary<string, List<FormControl>>(
                StringComparer.InvariantCulture
            );
            ExtractBpfEntityControls(doc.RootElement, byEntity);
            return byEntity.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<FormControl>)kvp.Value,
                StringComparer.InvariantCulture
            );
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<FormControl>>(
                StringComparer.InvariantCulture
            );
        }
    }

    private static void ExtractBpfEntityControls(
        JsonElement element,
        IDictionary<string, List<FormControl>> byEntity
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                if (TryGetNestedProperty(element, ["steps", "list"], out var listElement))
                {
                    ExtractBpfEntitySteps(listElement, byEntity);
                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    ExtractBpfEntityControls(property.Value, byEntity);
                }

                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractBpfEntityControls(item, byEntity);
                }

                break;
        }
    }

    private static void ExtractBpfEntitySteps(
        JsonElement listElement,
        IDictionary<string, List<FormControl>> byEntity
    )
    {
        if (listElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var step in listElement.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (
                TryGetStringProperty(step, "__class", out var className)
                && className.StartsWith("EntityStep", StringComparison.InvariantCulture)
                && TryGetStringProperty(step, "description", out var entityName)
                && !string.IsNullOrWhiteSpace(entityName)
            )
            {
                if (!byEntity.TryGetValue(entityName, out var controls))
                {
                    controls = [];
                    byEntity[entityName] = controls;
                }

                if (TryGetNestedProperty(step, ["steps", "list"], out var entitySteps))
                {
                    ExtractBpfControlsForEntity(entitySteps, controls);
                }
            }

            if (TryGetNestedProperty(step, ["steps", "list"], out var nestedList))
            {
                ExtractBpfEntitySteps(nestedList, byEntity);
            }
        }
    }

    private static void ExtractBpfControlsForEntity(
        JsonElement element,
        ICollection<FormControl> controls
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
            {
                foreach (var item in element.EnumerateArray())
                {
                    ExtractBpfControlsForEntity(item, controls);
                }

                break;
            }
            case JsonValueKind.Object:
            {
                if (!TryGetStringProperty(element, "__class", out var className))
                {
                    break;
                }

                if (
                    className.StartsWith("ControlStep", StringComparison.InvariantCulture)
                    && TryGetStringProperty(element, "dataFieldName", out var dataFieldName)
                    && !string.IsNullOrWhiteSpace(dataFieldName)
                )
                {
                    var controlId = TryGetStringProperty(element, "controlId", out var cid)
                        ? cid
                        : dataFieldName;
                    var classId = TryGetStringProperty(element, "classId", out var cls)
                        ? NormalizeGuidText(cls)
                        : null;
                    var kind = ResolveControlKind(controlId, classId);

                    controls.Add(
                        new FormControl(
                            $"header_process_{dataFieldName}",
                            dataFieldName,
                            classId,
                            kind,
                            null,
                            true,
                            null
                        )
                    );

                    break;
                }

                if (
                    className.StartsWith("PageStep", StringComparison.InvariantCulture)
                    || className.StartsWith("StageStep", StringComparison.InvariantCulture)
                    || className.StartsWith("StepStep", StringComparison.InvariantCulture)
                    || className.StartsWith("EntityStep", StringComparison.InvariantCulture)
                )
                {
                    if (TryGetNestedProperty(element, ["steps", "list"], out var nestedList))
                    {
                        ExtractBpfControlsForEntity(nestedList, controls);
                    }
                }

                break;
            }
        }
    }

    private static bool TryGetStringProperty(JsonElement obj, string propertyName, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetNestedStringProperty(
        JsonElement obj,
        IReadOnlyList<string> path,
        out string value
    )
    {
        value = string.Empty;
        var current = obj;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNestedProperty(
        JsonElement obj,
        IReadOnlyList<string> path,
        out JsonElement value
    )
    {
        value = obj;
        var current = obj;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static string MapFormType(int formType)
    {
        return formType switch
        {
            0 => "Dashboard",
            1 => "AppointmentBook",
            2 => "Main",
            3 => "MiniCampaignBO",
            4 => "Preview",
            5 => "Mobile",
            6 => "Quick",
            7 => "QuickCreate",
            8 => "Dialog",
            9 => "TaskFlowForm",
            10 => "InteractionCentricDashboard",
            11 => "Card",
            12 => "MainInteractionCentric",
            100 => "Other",
            101 => "MainBackup",
            102 => "AppointmentBookBackup",
            _ => "Other",
        };
    }

    private static void WriteViewFiles(
        string outputDirectory,
        string viewNamespace,
        IReadOnlyList<TableModel> tables
    )
    {
        var viewDir = Path.Combine(outputDirectory, "View");
        Directory.CreateDirectory(viewDir);

        foreach (var table in tables.OrderBy(t => t.LogicalName, StringComparer.InvariantCulture))
        {
            var entityDir = Path.Combine(viewDir, table.LogicalName);
            Directory.CreateDirectory(entityDir);

            var lines = new List<string>
            {
                "// <auto-generated />",
                $"declare namespace {viewNamespace}.{table.LogicalName} {{",
                "  interface DefaultView {",
            };

            foreach (
                var column in table.Columns.OrderBy(
                    c => c.LogicalName,
                    StringComparer.InvariantCulture
                )
            )
            {
                lines.Add($"    {column.LogicalName}: {MapTsType(column)};");
            }

            lines.Add("  }");
            lines.Add("}");

            File.WriteAllLines(Path.Combine(entityDir, "Default.d.ts"), lines);
        }
    }

    private static void WriteJavaScriptLibraries(
        string jsLibPath,
        TypeScriptGenerationOptions options
    )
    {
        Directory.CreateDirectory(jsLibPath);

        if (!string.IsNullOrWhiteSpace(options.WebNamespace))
        {
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.web.js",
                Path.Combine(jsLibPath, "dg.xrmquery.web.js")
            );
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.web.min.js",
                Path.Combine(jsLibPath, "dg.xrmquery.web.min.js")
            );
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.web.promise.min.js",
                Path.Combine(jsLibPath, "dg.xrmquery.web.promise.min.js")
            );
        }

        if (!string.IsNullOrWhiteSpace(options.RestNamespace))
        {
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.rest.js",
                Path.Combine(jsLibPath, "dg.xrmquery.rest.js")
            );
            TryWriteResourceDirect(
                "Dist/dg.xrmquery.rest.min.js",
                Path.Combine(jsLibPath, "dg.xrmquery.rest.min.js")
            );
        }
    }

    private static void WriteTypeScriptLibraries(
        string tsLibPath,
        TypeScriptGenerationOptions options
    )
    {
        Directory.CreateDirectory(tsLibPath);

        if (!string.IsNullOrWhiteSpace(options.WebNamespace))
        {
            TryWriteResourceDirect(
                "dg.xrmquery.web.ts",
                Path.Combine(tsLibPath, "dg.xrmquery.web.ts")
            );
        }

        if (!string.IsNullOrWhiteSpace(options.RestNamespace))
        {
            TryWriteResourceDirect(
                "dg.xrmquery.rest.ts",
                Path.Combine(tsLibPath, "dg.xrmquery.rest.ts")
            );
        }
    }

    private static string MapTsType(ColumnModel column)
    {
        return column switch
        {
            StringColumnModel or MemoColumnModel => "string | null",
            IntegerColumnModel or BigIntColumnModel or DecimalColumnModel or DoubleColumnModel =>
                "number | null",
            MoneyColumnModel => "number | null",
            BooleanColumnModel or BooleanManagedColumnModel => "boolean | null",
            DateTimeColumnModel => "Date | null",
            LookupColumnModel => "Xrm.LookupValue[] | null",
            PartyListColumnModel => "Xrm.LookupValue[] | null",
            UniqueIdentifierColumnModel or PrimaryIdColumnModel => "string | null",
            EnumColumnModel enumColumn when enumColumn.IsMultiSelect => "number[] | null",
            EnumColumnModel => "number | null",
            FileColumnModel or ImageColumnModel => "string | null",
            ManagedColumnModel => "unknown",
            _ => "unknown",
        };
    }

    private static string SanitizeIdentifier(string input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        var chars = input.Where(ch => ch == '_' || char.IsLetterOrDigit(ch)).ToArray();
        var value = chars.Length == 0 ? fallback : new string(chars);
        if (char.IsDigit(value[0]))
        {
            value = "_" + value;
        }

        if (TypeScriptKeywords.Contains(value))
        {
            value = "_" + value;
        }

        return value;
    }

    private static readonly HashSet<string> TypeScriptKeywords =
    [
        "import",
        "export",
        "class",
        "enum",
        "var",
        "for",
        "if",
        "else",
        "const",
        "true",
        "false",
    ];

    private static void WriteResourceDirect(string relativeResourcePath, string outputPath)
    {
        var lines = ReadResourceLines(relativeResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllLines(outputPath, lines);
    }

    private static bool TryWriteResourceDirect(string relativeResourcePath, string outputPath)
    {
        var lines = ReadResourceLinesOrNull(relativeResourcePath);
        if (lines == null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllLines(outputPath, lines);
        return true;
    }

    private static List<string> ReadResourceLines(string relativeResourcePath)
    {
        var lines = ReadResourceLinesOrNull(relativeResourcePath);
        if (lines == null)
        {
            throw new FileNotFoundException(
                $"Embedded TypeScript resource not found: {relativeResourcePath}"
            );
        }

        return lines;
    }

    private static List<string>? ReadResourceLinesOrNull(string relativeResourcePath)
    {
        var assembly = typeof(TypeScriptLegacyArtifactGenerator).Assembly;
        var expectedSuffix = relativeResourcePath.Replace('/', '.');

        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n =>
                n.StartsWith(ResourcePrefix, StringComparison.InvariantCulture)
                && n.EndsWith(expectedSuffix, StringComparison.InvariantCulture)
            );

        if (resourceName == null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded stream is null for: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            lines.Add(reader.ReadLine() ?? string.Empty);
        }

        return lines;
    }
}
