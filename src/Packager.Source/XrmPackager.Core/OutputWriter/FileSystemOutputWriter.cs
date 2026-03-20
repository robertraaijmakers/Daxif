using XrmPackager.Core.Formatting;
using XrmPackager.Core.Generation;

namespace XrmPackager.Core.Output;

public class FileSystemOutputWriter : IOutputWriter
{
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
            foreach (var file in Directory.GetFiles(outputDirectory))
            {
                File.Delete(file);
            }

            foreach (var dir in Directory.GetDirectories(outputDirectory))
            {
                Directory.Delete(dir, true);
            }
        }
        else
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var writtenFiles = new List<string>(filesList.Count);

        foreach (var file in filesList)
        {
            var filePath = Path.Combine(outputDirectory, file.Filename);
            var directoryPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Unable to determine directory path for file creation.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(filePath, file.Content);
            writtenFiles.Add(filePath);
        }

        _formatter.FormatFiles(writtenFiles);
    }
}
