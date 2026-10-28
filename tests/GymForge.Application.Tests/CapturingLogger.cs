using Microsoft.Extensions.Logging;

namespace GymForge.Application.Tests;

/// <summary>Logger que solo recuerda los niveles emitidos, para afirmar que se avisó (o no).</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogLevel> Levels { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
}
