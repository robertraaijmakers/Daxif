using System.Diagnostics;

namespace XrmPackager.Core.Formatting;

/// <summary>
/// Formats C# code using csharpier command-line tool.
/// Requires csharpier to be installed globally or locally via npm/dotnet tools.
/// </summary>
public class CsharpierFormatter : ICodeFormatter
{
    private readonly ILogger _logger;

    public CsharpierFormatter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Formats the given C# code using csharpier.
    /// Falls back to returning the original code if csharpier is not available.
    /// </summary>
    public string Format(string code)
    {
        try
        {
            // Create a temporary file to store the code
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, code);

                // Run csharpier on the temporary file
                var startInfo = new ProcessStartInfo
                {
                    FileName = "csharpier",
                    Arguments = $"\"{tempFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.Warning("csharpier process could not be started. Returning unformatted code.");
                    return code;
                }

                process.WaitForExit(5000); // 5 second timeout

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    _logger.Warning($"csharpier formatting failed: {error}. Returning unformatted code.");
                    return code;
                }

                // Read the formatted code
                var formattedCode = File.ReadAllText(tempFile);
                return formattedCode;
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignore errors deleting temp file
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error formatting code with csharpier: {ex.Message}. Returning unformatted code.");
            return code;
        }
    }
}
