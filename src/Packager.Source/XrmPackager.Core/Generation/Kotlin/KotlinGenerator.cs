namespace XrmPackager.Core.Generation.Kotlin;

using Microsoft.PowerPlatform.Dataverse.Client;
using XrmPackager.Core.Domain;
using XrmPackager.Core.Metadata;

public sealed class KotlinGenerator
{
    private readonly ILogger _logger;

    public KotlinGenerator(ILogger logger)
    {
        _logger = logger;
    }

    public void Generate(ServiceClient client, KotlinGenerationOptions options)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        var fetchConfig = new XrmFetchConfig(
            string.IsNullOrWhiteSpace(options.SolutionName)
                ? Array.Empty<string>()
                : new[] { options.SolutionName },
            options.Entities,
            string.Empty,
            new Dictionary<string, string>(StringComparer.InvariantCulture));

        var metadataFactory = new DataverseMetadataSourceFactory(client);
        var fetcher = metadataFactory.CreateFetcher(MetadataSourceType.Dataverse, fetchConfig);

        var tables = fetcher.FetchMetadataAsync().GetAwaiter().GetResult().ToList();

        WriteAnnotationsFile(outputPath, options.BasePackage);
        WriteEntityFiles(outputPath, options.BasePackage, tables);
        WriteEnumFiles(outputPath, options.BasePackage, tables);

        _logger.Info($"Kotlin entities generated to: {outputPath}");
        _logger.Info($"Tables: {tables.Count}");
    }

    private static void WriteAnnotationsFile(string outputPath, string basePackage)
    {
        var coreDir = Path.Combine(outputPath, "core");
        Directory.CreateDirectory(coreDir);
        var content = KotlinAnnotationsContent.Build(basePackage);
        File.WriteAllText(Path.Combine(coreDir, "D365Annotations.kt"), content, System.Text.Encoding.UTF8);
    }

    private static void WriteEntityFiles(string outputPath, string basePackage, IEnumerable<TableModel> tables)
    {
        var entityDir = Path.Combine(outputPath, "entity");
        Directory.CreateDirectory(entityDir);

        foreach (var table in tables)
        {
            var className = KotlinNameHelper.ToClassName(table.SchemaName) + "Entity";
            var content = KotlinEntityCodeBuilder.Build(table, basePackage);
            File.WriteAllText(Path.Combine(entityDir, $"{className}.kt"), content, System.Text.Encoding.UTF8);
        }
    }

    private void WriteEnumFiles(string outputPath, string basePackage, IEnumerable<TableModel> tables)
    {
        var enumsDir = Path.Combine(outputPath, "entity", "enums");
        Directory.CreateDirectory(enumsDir);

        var globalEnums = tables
            .SelectMany(t => t.Columns)
            .OfType<EnumColumnModel>()
            .Where(c => !string.IsNullOrWhiteSpace(c.OptionsetName) && c.OptionsetValues.Count > 0)
            .GroupBy(c => c.OptionsetName, StringComparer.InvariantCulture)
            .Select(g => g.First())
            .ToList();

        foreach (var enumCol in globalEnums)
        {
            var className = KotlinNameHelper.ToClassName(enumCol.OptionsetName);
            var content = KotlinEnumCodeBuilder.Build(enumCol, basePackage);
            File.WriteAllText(Path.Combine(enumsDir, $"{className}.kt"), content, System.Text.Encoding.UTF8);
            _logger.Info($"Enum generated: {className}");
        }
    }
}
