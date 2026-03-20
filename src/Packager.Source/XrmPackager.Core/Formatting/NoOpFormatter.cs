namespace XrmPackager.Core.Formatting;

/// <summary>
/// No-op formatter that returns code unchanged.
/// Used when code formatting is disabled.
/// </summary>
public class NoOpFormatter : ICodeFormatter
{
    /// <summary>
    /// Returns the code unchanged.
    /// </summary>
    public string Format(string code)
    {
        return code;
    }
}
