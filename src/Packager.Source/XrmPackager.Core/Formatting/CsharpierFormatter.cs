using System.Diagnostics;

namespace XrmPackager.Core.Formatting;

/// <summary>
/// Formats C# code using the CSharpier CLI tool.
/// Automatically restores csharpier as a local dotnet tool if not available globally.
/// This ensures formatting works consistently across all developer environments and CI/CD.
/// </summary>
public class CsharpierFormatter : ICodeFormatter
{
    private sealed record FormatterCommand(string FileName, string ArgumentsPrefix);

    private sealed record ToolManifest(string WorkingDirectory, string ManifestPath);

    private readonly ILogger _logger;
    private readonly string _toolWorkingDirectory;
    private readonly ToolManifest? _toolManifest;
    private readonly FormatterCommand? _command;
    private bool _hasLoggedUnavailableWarning;

    public CsharpierFormatter(ILogger logger)
    {
        _logger = logger;
        _toolManifest = ResolveToolManifest();
        _toolWorkingDirectory = _toolManifest?.WorkingDirectory ?? Environment.CurrentDirectory;
        _command = ResolveFormatterCommand();
    }

    /// <summary>
    /// Formats the given C# code using CSharpier.
    /// Falls back to returning the original code if csharpier is not available.
    /// </summary>
    public string Format(string code)
    {
        if (_command is null)
        {
            LogFormatterUnavailable();
            return code;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), ".xrm-csharpier", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var tempFile = Path.Combine(tempDirectory, "Generated.cs");

            try
            {
                File.WriteAllText(tempFile, code);

                if (!TryRunFormatter(_command, new[] { tempFile }, out var error))
                {
                    _logger.Warning($"Error formatting code with csharpier: {error}");
                    return code;
                }

                var formattedCode = File.ReadAllText(tempFile);
                return formattedCode;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error formatting code with csharpier: {ex.Message}");
            return code;
        }
    }

    /// <summary>
    /// Formats the specified files in one batch operation using csharpier.
    /// </summary>
    public void FormatFiles(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (_command is null)
        {
            LogFormatterUnavailable();
            return;
        }

        var normalizedFilePaths = filePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (normalizedFilePaths.Count == 0)
        {
            return;
        }

        // Back up original content before formatting.
        // CSharpier formats files in-place; a crash mid-write leaves the file truncated on disk.
        // These backups allow us to restore all files if CSharpier fails.
        var backups = new Dictionary<string, string>(normalizedFilePaths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in normalizedFilePaths)
        {
            try
            {
                backups[filePath] = File.ReadAllText(filePath);
            }
            catch
            { /* skip unreadable files */
            }
        }

        if (!TryRunFormatter(_command, normalizedFilePaths, out var error))
        {
            _logger.Warning($"Error formatting {normalizedFilePaths.Count} files with csharpier: {error}. Restoring original files to prevent truncation.");
            foreach (var (filePath, content) in backups)
            {
                try
                {
                    File.WriteAllText(filePath, content);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to restore '{Path.GetFileName(filePath)}': {ex.Message}");
                }
            }
        }
        else
        {
            _logger.Info($"Formatted {normalizedFilePaths.Count} files with csharpier");
        }
    }

    private bool TryRunFormatter(FormatterCommand command, IEnumerable<string> filePaths, out string error)
    {
        try
        {
            var arguments = BuildArguments(command, filePaths);
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = arguments,
                WorkingDirectory = _toolWorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = $"Unable to start '{command.FileName}'.";
                return false;
            }

            // Read streams asynchronously before WaitForExit to prevent a deadlock.
            // If the process writes more output than the OS pipe buffer can hold (~64 KB),
            // it blocks waiting for a reader while we block waiting for it to exit.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(300000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures.
                }

                error = $"'{command.FileName}' timed out while formatting.";
                return false;
            }

            var stderr = stderrTask.GetAwaiter().GetResult();
            var stdout = stdoutTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                error = string.IsNullOrWhiteSpace(error) ? $"'{command.FileName}' exited with code {process.ExitCode}." : error.Trim();
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private FormatterCommand? ResolveFormatterCommand()
    {
        // Try csharpier directly
        if (CanStartProcess("csharpier", "--version", _toolWorkingDirectory))
        {
            return new FormatterCommand("csharpier", string.Empty);
        }

        // Try as dotnet tool globally
        if (CanStartProcess("dotnet", "csharpier --version", _toolWorkingDirectory))
        {
            return new FormatterCommand("dotnet", "csharpier ");
        }

        // Try to restore local tool
        if (TryRestoreLocalTools())
        {
            // Check again after restore
            if (CanStartProcess("dotnet", "tool run dotnet-csharpier -- --version", _toolWorkingDirectory))
            {
                _logger.Info("CSharpier restored locally and is now available.");
                return new FormatterCommand("dotnet", "tool run dotnet-csharpier -- ");
            }

            if (CanStartProcess("dotnet", "csharpier --version", _toolWorkingDirectory))
            {
                _logger.Info("CSharpier restored locally and is now available.");
                return new FormatterCommand("dotnet", "csharpier ");
            }
        }

        return null;
    }

    private bool TryRestoreLocalTools()
    {
        try
        {
            if (_toolManifest is null)
            {
                return false;
            }

            _logger.Info("Restoring local dotnet tools...");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool restore --tool-manifest \"{_toolManifest.ManifestPath}\"",
                WorkingDirectory = _toolManifest.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(60000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ToolManifest? ResolveToolManifest()
    {
        return FindToolsManifest(AppContext.BaseDirectory) ?? FindToolsManifest(Environment.CurrentDirectory);
    }

    private static ToolManifest? FindToolsManifest(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
        {
            return null;
        }

        var currentDir = new DirectoryInfo(startDirectory);

        while (currentDir != null)
        {
            var configManifest = Path.Combine(currentDir.FullName, ".config", "dotnet-tools.json");
            if (File.Exists(configManifest))
            {
                return new ToolManifest(currentDir.FullName, configManifest);
            }

            var rootManifest = Path.Combine(currentDir.FullName, "dotnet-tools.json");
            if (File.Exists(rootManifest))
            {
                return new ToolManifest(currentDir.FullName, rootManifest);
            }

            var legacyManifest = Path.Combine(currentDir.FullName, ".dotnet-tools.json");
            if (File.Exists(legacyManifest))
            {
                return new ToolManifest(currentDir.FullName, legacyManifest);
            }

            currentDir = currentDir.Parent;
        }

        return null;
    }

    private bool CanStartProcess(string fileName, string arguments, string? workingDirectory = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string BuildArguments(FormatterCommand command, IEnumerable<string> filePaths)
    {
        var quotedPaths = filePaths.Select(path => $"\"{path}\"");
        return $"{command.ArgumentsPrefix}{string.Join(" ", quotedPaths)}".Trim();
    }

    private void LogFormatterUnavailable()
    {
        if (_hasLoggedUnavailableWarning)
        {
            return;
        }

        _logger.Info("CSharpier not available. Formatting skipped. Ensure .dotnet-tools.json exists near the runtime and dotnet tool restore can run, or install csharpier globally.");
        _hasLoggedUnavailableWarning = true;
    }
}
