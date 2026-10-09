using System.Globalization;
using Elsa.Activities.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Spec 188, T106 (research R17, FR-019): the outbound request goes through the named <see cref="IHttpClientFactory"/>
/// client, whose logging handlers can write request headers at <see cref="LogLevel.Trace"/>. Captures every line the
/// host's logging produces at that level while <c>SendHttpRequest</c> sends an <c>Authorization</c> value, and asserts
/// none of them (message, structured fields, scope or exception text) contains it. Scope: the lines the factory's
/// handlers and the runtime wrote during this one send, on the pinned <c>Microsoft.Extensions.Http</c>, with structured
/// field values rendered the way the canary's log capture renders them. The factory's header log value redacts its
/// message text, but its structured field holds an array of the raw header values, which that rendering prints as the
/// array's type name; a sink that serializes field contents would print them (spec 188 tasks.md, T106 as built).
/// </summary>
public sealed class SendHttpRequestAuthorizationLoggingTests
{
    private const string HttpClientCategoryPrefix = "System.Net.Http.HttpClient";

    private readonly string _authorizationValue = $"Bearer canary-{Guid.NewGuid():N}";
    private readonly CapturingLoggerProvider _logs = new();
    private string[]? _sentAuthorization;

    [Fact]
    public async Task TraceLogsFromTheNamedClient_NeverContainTheAuthorizationValue()
    {
        await using var harness = NewBuilder()
            .WithFeature(services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)))
            .WithFeature(StubTransport(request =>
            {
                _sentAuthorization = request.Headers.GetValues("Authorization").ToArray();
                return Respond(System.Net.HttpStatusCode.OK, "hello");
            }))
            .Build(ActivityExecutionId);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(authorization: _authorizationValue)));

        run.AssertOutcomes(NodeId, "Done");
        var lines = _logs.Lines;
        // Precondition: the value was on the request that passed through the factory's logging handlers.
        Assert.Equal(_authorizationValue, Assert.Single(_sentAuthorization ?? []));
        // Precondition: the factory's handlers logged this send, so the absence below is not an unwired provider.
        Assert.Contains(lines, line => line.Category.StartsWith(HttpClientCategoryPrefix, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain(_authorizationValue, line.Text, StringComparison.Ordinal));
    }

    private sealed record CapturedLine(string Category, string Text);

    /// <summary>Records every log call and scope with its formatted message, structured pairs and exception text.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<CapturedLine> _lines = [];

        public IReadOnlyList<CapturedLine> Lines
        {
            get
            {
                lock (_lines)
                    return _lines.ToArray();
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void Dispose()
        {
        }

        private void Add(string category, string text)
        {
            lock (_lines)
                _lines.Add(new CapturedLine(category, text));
        }

        private static string Describe<TState>(TState state) =>
            state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}"))
                : Convert.ToString(state, CultureInfo.InvariantCulture) ?? string.Empty;

        private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Add(category, Describe(state));
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add(category, $"{formatter(state, exception)} {Describe(state)} {exception}");
        }
    }
}
