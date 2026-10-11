using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Cinerama.IntegrationTests.Infrastructure;

/// <summary>
/// Guarda TODO lo que la API escribe en sus registros (con los mismos filtros de nivel que en producción), para poder
/// comprobar que ningún secreto ni token termina ahí.
/// </summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose() { }

    private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            lines.Enqueue($"[scope] {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}");
    }
}
