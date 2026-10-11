using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Captura los errores NO controlados de la API (las respuestas 500). Un test que provoca un 500 sin querer falla
/// con la excepción original, en lugar de quedar con un "InternalServerError" sin explicación.
/// </summary>
public sealed class ServerErrorLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _errors = new();

    public ILogger CreateLogger(string categoryName) =>
        categoryName.EndsWith("GlobalExceptionHandler", StringComparison.Ordinal) ? new Capture(_errors) : NullLogger.Instance;

    public IReadOnlyList<string> Drain()
    {
        var list = new List<string>();
        while (_errors.TryDequeue(out var error))
            list.Add(error);
        return list;
    }

    public void AssertNone()
    {
        var errors = Drain();
        if (errors.Count > 0)
            throw new Xunit.Sdk.XunitException("La API respondió con errores no controlados (500):\n" + string.Join("\n---\n", errors));
    }

    public void Dispose() { }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class Capture(ConcurrentQueue<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                errors.Enqueue(formatter(state, exception) + (exception is null ? "" : "\n" + exception));
        }
    }
}
