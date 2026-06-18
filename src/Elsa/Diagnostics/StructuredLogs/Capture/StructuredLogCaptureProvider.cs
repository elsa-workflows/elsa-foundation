using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Capture;

/// <summary>
/// The <see cref="ILoggerProvider"/> that captures host log events into the diagnostics pipeline. Events
/// from this feature's own categories are ignored so capture cannot feed back on itself.
/// </summary>
public sealed class StructuredLogCaptureProvider : ILoggerProvider, ISupportExternalScope
{
    private const string OwnCategoryPrefix = "Elsa.Diagnostics.StructuredLogs";

    private readonly IStructuredLogSink _sink;
    private readonly IStructuredLogSourceProvider _sourceProvider;
    private readonly StructuredLogsOptions _options;
    private IExternalScopeProvider? _scopeProvider;

    public StructuredLogCaptureProvider(
        IStructuredLogSink sink,
        IStructuredLogSourceProvider sourceProvider,
        IOptions<StructuredLogsOptions> options)
    {
        _sink = sink;
        _sourceProvider = sourceProvider;
        _options = options.Value;
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (categoryName.StartsWith(OwnCategoryPrefix, StringComparison.Ordinal))
            return NullLogger.Instance;

        return new StructuredLogCapturingLogger(
            categoryName,
            _sink,
            _options,
            () => _sourceProvider.GetLocalSource().Id,
            () => _scopeProvider);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose()
    {
    }
}
