using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IntegrationTests;

public sealed class TestLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> _entries = new();

    public IReadOnlyCollection<TestLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, _entries);

    public void Clear() => _entries.Clear();

    public void Dispose()
    {
    }

    private sealed class SinkLogger(string categoryName, ConcurrentQueue<TestLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new TestLogEntry(categoryName, logLevel, formatter(state, exception)));
        }
    }
}

public sealed record TestLogEntry(string Category, LogLevel LogLevel, string Message);
