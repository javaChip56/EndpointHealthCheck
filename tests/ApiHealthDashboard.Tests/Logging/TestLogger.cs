using Microsoft.Extensions.Logging;

namespace ApiHealthDashboard.Tests.Logging;

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry
        {
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    internal sealed class LogEntry
    {
        public required LogLevel LogLevel { get; init; }

        public required string Message { get; init; }

        public Exception? Exception { get; init; }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
