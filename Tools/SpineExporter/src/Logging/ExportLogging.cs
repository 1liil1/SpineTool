namespace IdleGearDefence.SpineExporter;

public interface IExportLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);
}

public sealed class ConsoleExportLogger : IExportLogger
{
    public void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public void Warning(string message)
    {
        Console.WriteLine($"[WARN] {message}");
    }

    public void Error(string message)
    {
        Console.Error.WriteLine($"[ERROR] {message}");
    }
}

public sealed class CompositeExportLogger : IExportLogger, IDisposable
{
    private readonly IExportLogger _primary;
    private readonly StreamWriter _fileWriter;

    public CompositeExportLogger(IExportLogger primary, string logPath)
    {
        _primary = primary;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Directory.GetCurrentDirectory());
        _fileWriter = new StreamWriter(logPath, append: false)
        {
            AutoFlush = true
        };
    }

    public void Info(string message)
    {
        Write("INFO", message, _primary.Info);
    }

    public void Warning(string message)
    {
        Write("WARN", message, _primary.Warning);
    }

    public void Error(string message)
    {
        Write("ERROR", message, _primary.Error);
    }

    public void Dispose()
    {
        _fileWriter.Dispose();
    }

    private void Write(string level, string message, Action<string> primaryWriter)
    {
        primaryWriter(message);
        _fileWriter.WriteLine($"[{DateTimeOffset.Now:O}] [{level}] {message}");
    }
}
