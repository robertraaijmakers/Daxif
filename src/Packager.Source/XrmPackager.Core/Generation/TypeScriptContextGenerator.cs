namespace XrmPackager.Core.Generation;

using Microsoft.PowerPlatform.Dataverse.Client;
using Scriban.Runtime;
using XrmPackager.Core.Domain;
using XrmPackager.Core.Metadata;
using XrmPackager.Core.Templates;

public sealed class TypeScriptContextGenerator
{
    private readonly EmbeddedTemplateProvider _templateProvider;
    private readonly ILogger _logger;

    public TypeScriptContextGenerator(ILogger logger)
    {
        _logger = logger;
        _templateProvider = new EmbeddedTemplateProvider();
    }

    public void Generate(ServiceClient client, TypeScriptGenerationOptions options)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory =
            options.OneFile ? Path.GetDirectoryName(outputPath)
            : Path.HasExtension(outputPath) ? Path.GetDirectoryName(outputPath)
            : outputPath;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(
                $"Unable to determine output directory for '{options.OutputPath}'."
            );
        }

        Directory.CreateDirectory(outputDirectory);
        ClearExistingDeclarationFiles(outputDirectory);

        var fetchConfig = new XrmFetchConfig(
            string.IsNullOrWhiteSpace(options.SolutionName)
                ? Array.Empty<string>()
                : new[] { options.SolutionName },
            options.Entities,
            string.Empty,
            new Dictionary<string, string>(StringComparer.InvariantCulture)
        );

        var metadataFactory = new DataverseMetadataSourceFactory(client);
        var fetcher = metadataFactory.CreateFetcher(MetadataSourceType.Dataverse, fetchConfig);
        var tables = fetcher
            .FetchMetadataAsync()
            .GetAwaiter()
            .GetResult()
            .OrderBy(t => t.SchemaName, StringComparer.InvariantCulture)
            .ToList();

        if (options.OneFile)
        {
            var oneFileModel = new
            {
                Namespace = options.Namespace,
                Tables = tables.Select(MapTableForTemplate).ToList(),
            };

            var source = RenderTemplate("TypeScriptContext.scriban-ts", oneFileModel);
            File.WriteAllText(outputPath, source);

            _logger.Info($"TypeScript definitions generated: {outputPath}");
            _logger.Info($"Tables included: {tables.Count}");

            if (options.EmitLegacyResources)
            {
                TypeScriptLegacyArtifactGenerator.Generate(
                    outputDirectory,
                    client,
                    options,
                    tables,
                    _logger
                );
            }
            return;
        }

        if (options.EmitLegacyResources)
        {
            TypeScriptLegacyArtifactGenerator.Generate(
                outputDirectory,
                client,
                options,
                tables,
                _logger
            );
        }
        else
        {
            GenerateMultiFileDefinitions(outputDirectory, options.Namespace, tables);
        }

        _logger.Info($"TypeScript definitions generated in folder: {outputDirectory}");
        _logger.Info($"Tables included: {tables.Count}");
    }

    private void GenerateMultiFileDefinitions(
        string outputDirectory,
        string namespaceName,
        IReadOnlyList<TableModel> tables
    )
    {
        var entities = new List<(string Folder, string Name)>();

        foreach (var table in tables)
        {
            var safeName = ToTypeScriptIdentifier(table.SchemaName, "Entity");
            var folderPath = Path.Combine(outputDirectory, safeName);
            Directory.CreateDirectory(folderPath);

            var entityModel = new { Namespace = namespaceName, Table = MapTableForTemplate(table) };

            var entityContent = RenderTemplate("TypeScriptEntity.scriban-ts", entityModel);
            var entityFilePath = Path.Combine(folderPath, $"{safeName}.d.ts");
            File.WriteAllText(entityFilePath, entityContent);

            var indexContent = $"export * from './{safeName}';{Environment.NewLine}";
            File.WriteAllText(Path.Combine(folderPath, "index.d.ts"), indexContent);

            entities.Add((safeName, safeName));
        }

        var rootIndexModel = new
        {
            Entities = entities.Select(e => new { Folder = e.Folder, Name = e.Name }).ToList(),
        };

        var rootIndexContent = RenderTemplate("TypeScriptIndex.scriban-ts", rootIndexModel);
        File.WriteAllText(Path.Combine(outputDirectory, "index.d.ts"), rootIndexContent);
    }

    private string RenderTemplate(string templateName, object model)
    {
        var template = _templateProvider.GetTemplate(templateName);
        var context = new Scriban.TemplateContext(StringComparer.InvariantCulture)
        {
            LoopLimit = 0,
            MemberRenamer = member => member.Name,
        };

        var scriptObject = new ScriptObject(StringComparer.InvariantCulture);
        scriptObject.Import(model, renamer: member => member.Name);
        context.PushGlobal(scriptObject);
        context.TemplateLoader = _templateProvider;

        return template.Render(context);
    }

    private static object MapTableForTemplate(TableModel table)
    {
        return new
        {
            LogicalName = table.LogicalName,
            SchemaName = ToTypeScriptIdentifier(table.SchemaName, "Entity"),
            PrimaryIdAttribute = table.PrimaryIdAttribute,
            Columns = table
                .Columns.OrderBy(c => c.LogicalName, StringComparer.InvariantCulture)
                .Select(c => new
                {
                    LogicalName = c.LogicalName,
                    SchemaName = ToTypeScriptIdentifier(c.SchemaName, "Column"),
                    TsType = MapColumnType(c),
                })
                .ToList(),
        };
    }

    private static string MapColumnType(ColumnModel column)
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
            ManagedColumnModel managed => managed.ReturnType switch
            {
                "bool" or "Boolean" => "boolean | null",
                "int" or "long" or "decimal" or "double" => "number | null",
                "DateTime" => "Date | null",
                _ => "string | null",
            },
            _ => "unknown",
        };
    }

    private static string ToTypeScriptIdentifier(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var parts = raw.Split(new[] { '_', ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray()))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();

        var id = string.Concat(parts);
        if (string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        if (char.IsDigit(id[0]))
        {
            id = "_" + id;
        }

        return id;
    }

    private static void ClearExistingDeclarationFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        foreach (
            var file in Directory.EnumerateFiles(
                outputDirectory,
                "*.d.ts",
                SearchOption.AllDirectories
            )
        )
        {
            File.Delete(file);
        }
    }
}
