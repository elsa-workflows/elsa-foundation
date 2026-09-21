using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Capture;

/// <summary>
/// The <see cref="ILogger"/> that forwards a single category's events into the diagnostics sink. It never
/// throws into the host logging path: sink failures are swallowed.
/// </summary>
public sealed class StructuredLogCapturingLogger : ILogger
{
    private readonly string _category;
    private readonly IStructuredLogSink _sink;
    private readonly StructuredLogsOptions _options;
    private readonly Func<string> _sourceIdAccessor;
    private readonly Func<IExternalScopeProvider?> _scopeProviderAccessor;

    public StructuredLogCapturingLogger(
        string category,
        IStructuredLogSink sink,
        StructuredLogsOptions options,
        Func<string> sourceIdAccessor,
        Func<IExternalScopeProvider?> scopeProviderAccessor)
    {
        _category = category;
        _sink = sink;
        _options = options;
        _sourceIdAccessor = sourceIdAccessor;
        _scopeProviderAccessor = scopeProviderAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        _scopeProviderAccessor()?.Push(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        try
        {
            var message = formatter(state, exception);
            var entry = StructuredLogEntryFactory.Create(
                _options,
                _sourceIdAccessor(),
                _category,
                logLevel,
                eventId,
                state,
                exception,
                message,
                _scopeProviderAccessor());

            _sink.Emit(entry);
        }
        catch
        {
            // Diagnostics capture must never throw into the host logging path.
        }
    }
}
