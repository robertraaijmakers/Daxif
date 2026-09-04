namespace XrmPackager.Core.Generation.Kotlin;

using System.Text;

public static class KotlinNameHelper
{
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "as", "break", "class", "continue", "do", "else", "false", "for", "fun", "if",
        "in", "interface", "is", "null", "object", "package", "return", "super", "this",
        "throw", "true", "try", "typealias", "typeof", "val", "var", "when", "while",
        "by", "catch", "constructor", "delegate", "dynamic", "field", "file", "finally",
        "get", "import", "init", "param", "property", "receiver", "set", "setparam",
        "value", "where", "actual", "abstract", "annotation", "companion", "crossinline",
        "data", "enum", "expect", "external", "final", "infix", "inline", "inner",
        "internal", "lateinit", "noinline", "open", "operator", "out", "override",
        "private", "protected", "public", "reified", "sealed", "suspend", "tailrec", "vararg"
    };

    // Converts Dataverse schema name (e.g. "bol_PartnerId") to Kotlin property name (e.g. "bolPartnerId")
    public static string ToPropertyName(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName)) return "_unknown";

        var parts = schemaName.Split('_');
        var sb = new StringBuilder();

        foreach (var part in parts)
        {
            var cleaned = string.Concat(part.Where(char.IsLetterOrDigit));
            if (string.IsNullOrEmpty(cleaned)) continue;

            if (sb.Length == 0)
                sb.Append(cleaned.ToLowerInvariant());
            else
            {
                sb.Append(char.ToUpperInvariant(cleaned[0]));
                if (cleaned.Length > 1) sb.Append(cleaned[1..]);
            }
        }

        var result = sb.ToString();
        if (string.IsNullOrEmpty(result)) return "_unknown";
        if (char.IsDigit(result[0])) result = "_" + result;
        if (ReservedKeywords.Contains(result)) result += "_";
        return result;
    }

    private static readonly Dictionary<string, string> OrdinalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "1ST", "FIRST" }, { "2ND", "SECOND" }, { "3RD", "THIRD" },
        { "4TH", "FOURTH" }, { "5TH", "FIFTH" }, { "6TH", "SIXTH" },
        { "7TH", "SEVENTH" }, { "8TH", "EIGHTH" }, { "9TH", "NINTH" },
        { "10TH", "TENTH" }, { "11TH", "ELEVENTH" }, { "12TH", "TWELFTH" },
    };

    // Converts a display label to SCREAMING_SNAKE_CASE enum constant name
    public static string ToEnumConstantName(string label, int value)
    {
        if (string.IsNullOrWhiteSpace(label)) return $"OPTION_{value}";

        var sb = new StringBuilder();
        var lastWasUnderscore = true;

        foreach (var c in label)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        var result = sb.ToString().TrimEnd('_');
        if (string.IsNullOrEmpty(result)) return $"OPTION_{value}";

        // Ordinal labels (1ST, 2ND …) → word form (FIRST, SECOND …)
        if (OrdinalWords.TryGetValue(result, out var ordinalWord)) return ordinalWord;

        // Other digit-leading names: prefix with N (not _) to satisfy ktlint enum-entry-name-case
        if (char.IsDigit(result[0])) result = "N" + result;

        return result;
    }

    // Converts schema name or optionset name to PascalCase class name
    public static string ToClassName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "UnknownClass";

        var parts = name.Split('_');
        var sb = new StringBuilder();

        foreach (var part in parts)
        {
            var cleaned = string.Concat(part.Where(char.IsLetterOrDigit));
            if (string.IsNullOrEmpty(cleaned)) continue;
            sb.Append(char.ToUpperInvariant(cleaned[0]));
            if (cleaned.Length > 1) sb.Append(cleaned[1..]);
        }

        var result = sb.ToString();
        if (string.IsNullOrEmpty(result)) return "UnknownClass";
        if (char.IsDigit(result[0])) result = "C" + result;
        return result;
    }

    // Escapes a string for use in a Kotlin string literal
    public static string EscapeString(string s) =>
        (s ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "");
}
