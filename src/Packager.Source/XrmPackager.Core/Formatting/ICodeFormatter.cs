namespace XrmPackager.Core.Formatting;

/// <summary>
/// Interface for code formatters that format generated C# code.
/// </summary>
public interface ICodeFormatter
{
    /// <summary>
    /// Formats the given C# code string.
    /// </summary>
    /// <param name="code">The C# code to format.</param>
    /// <returns>The formatted C# code, or the original code if formatting fails.</returns>
    string Format(string code);

    /// <summary>
    /// Formats the specified files in one batch operation.
    /// </summary>
    /// <param name="filePaths">The absolute file paths to format.</param>
    void FormatFiles(IEnumerable<string> filePaths);
}
