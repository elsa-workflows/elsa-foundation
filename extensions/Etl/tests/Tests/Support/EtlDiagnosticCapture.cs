using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Tests.Support;

/// <summary>Observes raw diagnostic output; deliberately does not mask the values under test.</summary>
internal sealed class EtlDiagnosticCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly Activity _parent = new Activity("etl-diagnostic-test").SetIdFormat(ActivityIdFormat.W3C).Start();
    private readonly ActivityListener _listener;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    public ConcurrentQueue<CapturedActivity> Activities { get; } = new();
    public ConcurrentQueue<CapturedLog> Logs { get; } = new();
    public TaskCompletionSource WorkerWarning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EtlDiagnosticCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http" || source.Name.StartsWith("Elsa.Etl", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId != _parent.TraceId) return;
                Activities.Enqueue(new(activity.Source.Name, activity.OperationName, activity.GetTagItem("url.full")?.ToString(),
                    JsonSerializer.Serialize(new
                    {
                        activity.DisplayName, activity.Status, activity.StatusDescription,
                        Tags = activity.TagObjects.ToArray(), Baggage = activity.Baggage.ToArray(),
                        Events = activity.Events.Select(item => new { item.Name, Tags = item.Tags.ToArray() }).ToArray(),
                        Links = activity.Links.Select(item => new { item.Context, Tags = item.Tags?.ToArray() }).ToArray()
                    })));
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public void Dispose() { _listener.Dispose(); _parent.Dispose(); }

    public static void AssertAbsent(string observed, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.DoesNotContain(marker, observed, StringComparison.Ordinal);
    }

    public void AssertPlatformRequests(int expectedCount, string endpointWithoutQuery, params string[] markers)
    {
        // These fixtures verify the platform default; they must not silently change a host opt-out.
        Assert.False(AppContext.TryGetSwitch("System.Net.Http.DisableUriRedaction", out var disabled) && disabled);
        var environment = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION");
        Assert.False(environment is "1" || string.Equals(environment, "true", StringComparison.OrdinalIgnoreCase));
        var requests = Activities.Where(item => item.Source == "System.Net.Http").ToArray();
        Assert.Equal(expectedCount, requests.Length);
        Assert.All(requests, item =>
        {
            Assert.Equal("System.Net.Http.HttpRequestOut", item.Operation);
            // The host still sees origin and path. Only the query is redacted by this platform default.
            Assert.Equal(endpointWithoutQuery + "?*", item.Url);
            AssertAbsent(item.Text, markers);
        });
    }

    internal sealed record CapturedActivity(string Source, string Operation, string? Url, string Text);
    internal sealed record CapturedLog(string Category, LogLevel Level, string Message, string Details, string? Exception);

    private sealed class CaptureLogger(EtlDiagnosticCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => category.StartsWith("Elsa.Etl", StringComparison.Ordinal)
            || category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var scopes = new List<object?>();
            owner._scopes.ForEachScope((value, values) => values.Add(value), scopes);
            var message = formatter(state, exception);
            owner.Logs.Enqueue(new(category, logLevel, message,
                JsonSerializer.Serialize(new { eventId, State = state, Scopes = scopes }), exception?.ToString()));
            if (message.StartsWith("ETL attempt stopped after ", StringComparison.Ordinal))
                owner.WorkerWarning.TrySetResult();
        }
    }
}
