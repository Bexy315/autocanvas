using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.TestSupport;

/// <summary>Collects log entries of every category, for asserting on what was logged.</summary>
public sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message)> entries = new();

    /// <summary>Everything logged so far, in order.</summary>
    public IReadOnlyCollection<(LogLevel Level, string Category, string Message)> Entries => entries;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(LogLevel, string, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, category, formatter(state, exception)));
    }
}
