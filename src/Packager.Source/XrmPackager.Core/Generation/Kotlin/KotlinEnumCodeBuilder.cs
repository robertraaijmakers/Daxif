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
        sb.AppendLine($"enum class {className}(val code: Int) {{");

        var options = enumColumn.OptionsetValues
            .OrderBy(kvp => kvp.Key)
            .ToList();

        var usedConstants = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < options.Count; i++)
        {
            var (value, label) = options[i];
            var constantName = GetUniqueConstantName(label, value, usedConstants);
            usedConstants.Add(constantName);
            var comma = i < options.Count - 1 ? "," : ";";
            sb.AppendLine($"    {constantName}({value}){comma}");
        }

        sb.AppendLine();
        sb.AppendLine("    companion object {");
        sb.AppendLine("        fun fromCode(code: Int?) = entries.find { it.code == code }");
        sb.AppendLine();
        sb.AppendLine("        fun fromCodeOrThrow(code: Int) = fromCode(code)");
        sb.AppendLine("            ?: throw IllegalArgumentException(\"Unknown code: $code\")");
        sb.AppendLine("    }");
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
