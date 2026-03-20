using System.Text;
using XrmPackager.Core.Formatting;
using XrmPackager.Core.Generation;

namespace XrmPackager.Core.Output;

public class FileSystemOutputWriter : IOutputWriter
{
    private static readonly string[] GeneratedFolders = { "helpers", "optionsets", "queries", "tables" };

    private readonly ICodeFormatter _formatter;

    public FileSystemOutputWriter(ICodeFormatter? formatter = null)
    {
        _formatter = formatter ?? new NoOpFormatter();
    }

    public void WriteFiles(IEnumerable<GeneratedFile> files, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);

        var filesList = files.ToList();

        if (Directory.Exists(outputDirectory))
        {
            foreach (var folder in GeneratedFolders)
            {
                var folderPath = Path.Combine(outputDirectory, folder);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var writtenFiles = new List<string>(filesList.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in filesList)
        {
            var filePath = Path.Combine(outputDirectory, file.Filename);
            var directoryPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Unable to determine directory path for file creation.");
            Directory.CreateDirectory(directoryPath);

            if (!seenPaths.Add(filePath))
            {
                throw new InvalidOperationException($"Duplicate generated output path detected: '{file.Filename}'.");
            }

            // Write atomically to avoid partial/corrupt files if the process is interrupted mid-write.
            var tempPath = Path.Combine(directoryPath, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, file.Content, Encoding.UTF8);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempPath, filePath);
            writtenFiles.Add(filePath);
        }

        _formatter.FormatFiles(writtenFiles);
    }
}
