namespace XrmPackager.Core.Generation;

using System.Text;
using Microsoft.PowerPlatform.Dataverse.Client;
using XrmPackager.Core.Formatting;
using XrmPackager.Core.Metadata;
using XrmPackager.Core.Output;

public sealed class CSharpContextGenerator
{
    private readonly ILogger _logger;

    public CSharpContextGenerator(ILogger logger)
    {
        _logger = logger;
    }

    public void Generate(ServiceClient client, ContextGenerationOptions options)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);

        if (options.OneFile)
        {
            var outputLooksLikeDirectory = Directory.Exists(outputPath) || !Path.HasExtension(outputPath);

            if (outputLooksLikeDirectory)
            {
                outputPath = Path.Combine(outputPath, "XrmContext.cs");
            }
        }

        var outputDirectory = options.OneFile ? Path.GetDirectoryName(outputPath) : outputPath;

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"Unable to determine output directory for '{options.OutputPath}'.");
        }

        Directory.CreateDirectory(outputDirectory);

        var serviceContextName = Path.GetFileNameWithoutExtension(outputPath);
        var fetchConfig = new XrmFetchConfig(
            string.IsNullOrWhiteSpace(options.SolutionName) ? Array.Empty<string>() : new[] { options.SolutionName },
            options.Entities,
            string.Empty,
            new Dictionary<string, string>(StringComparer.InvariantCulture)
        );

        var metadataFactory = new DataverseMetadataSourceFactory(client);
        var fetcher = metadataFactory.CreateFetcher(MetadataSourceType.Dataverse, fetchConfig);

        var tables = fetcher.FetchMetadataAsync().GetAwaiter().GetResult().ToList();
        var customApis = fetcher.FetchCustomApisAsync().GetAwaiter().GetResult().ToList();

        var generationConfig = new XrmGenerationConfig(
            outputDirectory,
            options.Namespace,
            serviceContextName,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.InvariantCulture),
            options.OneFile,
            true,
            options.ConsolidateHelpers,
            options.FormatOutput
        );

        var generator = new CSharpProxyGenerator();
        var files = generator.GenerateCode(tables, customApis, generationConfig).ToList();

        if (options.OneFile)
        {
            if (files.Count == 0)
            {
                throw new InvalidOperationException("No files were generated for one-file mode.");
            }

            var generatedMainFile = files.First();
            var content = generatedMainFile.Content;

            // Apply formatting if requested
            if (options.FormatOutput)
            {
                var formatter = new CsharpierFormatter(_logger);
                content = formatter.Format(content);
            }

            File.WriteAllText(outputPath, content, Encoding.UTF8);
            _logger.Info($"C# context generated: {outputPath}");
            _logger.Info($"Tables included: {tables.Count}");
            _logger.Info($"Custom APIs included: {customApis.Count}");
            return;
        }

        var formatter2 = options.FormatOutput ? new CsharpierFormatter(_logger) : null;
        var outputWriter = new FileSystemOutputWriter(formatter2);
        outputWriter.WriteFiles(files, outputDirectory);

        _logger.Info($"C# context generated to directory: {outputDirectory}");
        _logger.Info($"Files generated: {files.Count}");
        _logger.Info($"Tables included: {tables.Count}");
        _logger.Info($"Custom APIs included: {customApis.Count}");
    }
}
