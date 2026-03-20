using System.Text;
using XrmPackager.Core.Generation.Common;
using XrmPackager.Core.Generation.Mappers;

namespace XrmPackager.Core.Generation.Generators;

/// <summary>
/// Generates a consolidated XrmHelpers.cs file containing all helper classes
/// (DataversePropertyMetadataAttribute, OptionSetMetadataAttribute,
/// RelationshipMetadataAttribute, TableAttributeHelpers, ExtendedEntity)
/// </summary>
public class ConsolidatedHelperGenerator : BaseFileGenerator
{
    private static readonly string[] HelperClassNames = new[]
    {
        "DataversePropertyMetadataAttribute",
        "OptionSetMetadataAttribute",
        "RelationshipMetadataAttribute",
        "TableAttributeHelpers",
        "ExtendedEntity",
    };

    public IEnumerable<GeneratedFile> Generate(GenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return GenerateInternal(context);
    }

    private IEnumerable<GeneratedFile> GenerateInternal(GenerationContext context)
    {
        ValidateContext(context);

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("namespace " + context.Namespace + ";");
        contentBuilder.AppendLine();

        // Generate each helper class
        bool isFirst = true;
        foreach (var helperClassName in HelperClassNames)
        {
            if (!isFirst)
            {
                contentBuilder.AppendLine();
            }

            var templateModel = HelperFileMapper.MapToTemplateModel(helperClassName, context);
            var template = context.Templates.GetTemplate($"{helperClassName}.scriban-cs");
            var templateContext = CreateTemplateContext(templateModel, context.Templates);
            var helperContent = template.Render(templateContext);

            // Extract the class content (remove namespace declaration)
            var lines = helperContent.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var classContent = ExtractClassContent(lines);
            contentBuilder.AppendLine(classContent);

            isFirst = false;
        }

        yield return new GeneratedFile("XrmHelpers.cs", contentBuilder.ToString());
    }

    /// <summary>
    /// Extracts the class content from a file that starts with a namespace declaration.
    /// Removes the namespace and using statements to allow consolidation into a single file.
    /// </summary>
    private static string ExtractClassContent(string[] lines)
    {
        var contentLines = new List<string>();
        var inUsingStatements = true;
        var namespaceSkipped = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip using statements at the beginning
            if (inUsingStatements && trimmedLine.StartsWith("using ", StringComparison.Ordinal))
            {
                continue;
            }

            // Skip namespace declaration
            if (trimmedLine.StartsWith("namespace ", StringComparison.Ordinal))
            {
                namespaceSkipped = true;
                inUsingStatements = false;
                continue;
            }

            // Stop skipping once we encounter namespace
            if (trimmedLine.Length > 0)
            {
                inUsingStatements = false;
            }

            // Include all lines after namespace/using
            if (namespaceSkipped || (!inUsingStatements && !trimmedLine.StartsWith("using ")))
            {
                contentLines.Add(line);
            }
        }

        // Remove leading empty lines
        while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[0]))
        {
            contentLines.RemoveAt(0);
        }

        // Remove trailing empty lines
        while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[contentLines.Count - 1]))
        {
            contentLines.RemoveAt(contentLines.Count - 1);
        }

        return string.Join(Environment.NewLine, contentLines);
    }
}
