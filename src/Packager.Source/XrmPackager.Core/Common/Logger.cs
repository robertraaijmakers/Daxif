namespace XrmPackager.Core;

/// <summary>
/// Interface for logging output.
/// </summary>
public interface ILogger
{
    void Error(string message);
    void Warning(string message);
    void Info(string message);
    void Verbose(string message);
    void Debug(string message);
}

/// <summary>
/// Simple console logger implementation.
/// </summary>
public class ConsoleLogger : ILogger
{
    private readonly LogLevel _minLevel;

    public ConsoleLogger(LogLevel minLevel = LogLevel.Info)
    {
        _minLevel = minLevel;
    }

    public void Error(string message)
    {
        if (_minLevel >= LogLevel.Error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.ResetColor();
        }
    }

    public void Warning(string message)
    {
        if (_minLevel >= LogLevel.Warning)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {message}");
            Console.ResetColor();
        }
    }

    public void Info(string message)
    {
        if (_minLevel >= LogLevel.Info)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[INFO] {message}");
            Console.ResetColor();
        }
    }

    public void Verbose(string message)
    {
        if (_minLevel >= LogLevel.Verbose)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"[VERB] {message}");
            Console.ResetColor();
        }
    }

    public void Debug(string message)
    {
        if (_minLevel >= LogLevel.Debug)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[DEBUG] {message}");
            Console.ResetColor();
        }
    }
}

/// <summary>
/// Null logger that discards all messages.
/// </summary>
public class NullLogger : ILogger
{
    public void Error(string message) { }

    public void Warning(string message) { }

    public void Info(string message) { }

    public void Verbose(string message) { }

    public void Debug(string message) { }
}
