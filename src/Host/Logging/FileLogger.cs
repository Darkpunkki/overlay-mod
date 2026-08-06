using System.Text;

namespace OverlayMod.Host.Logging;

/// <summary>
/// Appends log lines to a file.
///
/// The published build is a windowed application with no console, so without
/// this a failure to attach, a bad route file or an unregistered hotkey would
/// vanish silently. The log is the only place those show up, which makes it the
/// first thing to ask for when something misbehaves.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Truncate at this size on startup, so the file cannot grow without bound.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Keep the tail rather than the head: recent lines are the useful ones.
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                var kept = File.ReadLines(path).TakeLast(2000).ToArray();
                File.WriteAllLines(path, kept);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string line)
    {
        try
        {
            lock (_gate) File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that breaks a run.
        }
    }

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            // Full namespaces make every line unreadable; the leaf is enough.
            _category = category[(category.LastIndexOf('.') + 1)..];
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Short(logLevel)} {_category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;

            _provider.Write(line);
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "trc",
            LogLevel.Debug => "dbg",
            LogLevel.Information => "inf",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
