namespace XrmPackager.Core.Generation.Kotlin;

using System.Text;
using XrmPackager.Core.Domain;

public static class KotlinEnumCodeBuilder
{
    public static string Build(EnumColumnModel enumColumn, string basePackage)
    {
        var enumsPackage = $"{basePackage}.entity.enums";
        var className = KotlinNameHelper.ToClassName(enumColumn.OptionsetName);

        var sb = new StringBuilder();
        sb.AppendLine($"package {enumsPackage}");
        sb.AppendLine();
        sb.AppendLine($"enum class {className}(override val code: Int, override val internalLabel: String, override val externalLabel: String) : LabeledEnum {{");

        var options = enumColumn.OptionsetValues
            .OrderBy(kvp => kvp.Key)
            .ToList();

        var usedConstants = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < options.Count; i++)
        {
            var (value, label) = options[i];
            var constantName = GetUniqueConstantName(label, value, usedConstants);
            usedConstants.Add(constantName);
            var comma = i < options.Count - 1 ? "," : "";
            var escapedLabel = KotlinNameHelper.EscapeString(label);
            var externalValue = enumColumn.OptionExternalValues.TryGetValue(value, out var ev)
                ? KotlinNameHelper.EscapeString(ev)
                : escapedLabel;
            sb.AppendLine($"    {constantName}({value}, \"{escapedLabel}\", \"{externalValue}\"){comma}");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetUniqueConstantName(string label, int value, HashSet<string> usedNames)
    {
        var baseName = KotlinNameHelper.ToEnumConstantName(label, value);
        if (!usedNames.Contains(baseName)) return baseName;
        var i = 1;
        while (usedNames.Contains($"{baseName}_{i}")) i++;
        return $"{baseName}_{i}";
    }
}
