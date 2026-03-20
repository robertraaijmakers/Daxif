using System.Security;
using System.Text;

namespace XrmPackager.Core.Generation.Common;

internal static class TemplateTextSanitizer
{
    public static string CSharpString(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        continue;
                    }

                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    public static string XmlDoc(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var cleaned = RemoveInvalidXmlChars(text);
        return SecurityElement.Escape(cleaned) ?? string.Empty;
    }

    private static string RemoveInvalidXmlChars(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

internal sealed class TemplateTextFunctions
{
    public string xml_doc(object? value)
    {
        return TemplateTextSanitizer.XmlDoc(value);
    }

    public string csharp_string(object? value)
    {
        return TemplateTextSanitizer.CSharpString(value);
    }
}
