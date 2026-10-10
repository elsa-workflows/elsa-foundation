using System.Collections;
using System.Globalization;
using System.Net;
using System.Text;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Spec 188, T106 (research R17, FR-019): the outbound request goes through the named <see cref="IHttpClientFactory"/>
/// client, whose default logging handlers carry raw header values as structured state at <see cref="LogLevel.Trace"/>.
/// <see cref="ActivitiesHttpFeature"/> replaces them with one logger that writes no header. Captures every log call and
/// scope the host's logging produces at <see cref="LogLevel.Trace"/> while <c>SendHttpRequest</c> sends an
/// <c>Authorization</c> value, serializing each one's formatted message, its structured state (every pair, its value
/// expanded through nested pairs and collections) and its exception, and asserts none of them contains the value.
/// Scope: the lines and scopes written during one successful and one failed send, through the composition
/// <see cref="ActivitiesHttpFeature"/> registers.
/// </summary>
public sealed class SendHttpRequestAuthorizationLoggingTests
{
    // The default logical handler's category, which the replaced logging no longer writes for this client.
    private const string DefaultLogicalHandlerCategory = "System.Net.Http.HttpClient." + HttpActivityConstants.HttpClientName + ".LogicalHandler";

    private readonly string _authorizationValue = NewHeaderValue();
    private readonly CapturingLoggerProvider _logs = new();
    private string[]? _sentAuthorization;

    [Fact]
    public async Task TraceLogMessagesStateAndScopes_OfASend_DoNotContainTheAuthorizationValue()
    {
        var run = await SendAsync(() => Respond(HttpStatusCode.OK, "hello"));

        run.AssertOutcomes(NodeId, "Done");
        AssertLogsDoNotContainTheValue();
    }

    [Fact]
    public async Task TraceLogMessagesStateAndScopes_OfAFailedSend_DoNotContainTheAuthorizationValue()
    {
        // The exception's message carries the value, so a logger that wrote the message or the exception would hold it.
        var run = await SendAsync(() => throw new HttpRequestException($"refused {_authorizationValue}"));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Failed);
        AssertLogsDoNotContainTheValue();
        Assert.Contains(_logs.Lines, line => line.Category == ClientLoggerCategory && line.Text.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
    }

    private async Task<WorkflowExecutionRun> SendAsync(Func<HttpResponseMessage> respond)
    {
        await using var harness = NewBuilder()
            .WithFeature(services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)))
            .WithFeature(StubTransport(request =>
            {
                _sentAuthorization = request.Headers.GetValues("Authorization").ToArray();
                return respond();
            }))
            .Build(ActivityExecutionId);

        return await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(authorization: _authorizationValue)));
    }

    private void AssertLogsDoNotContainTheValue()
    {
        var lines = _logs.Lines;
        // Precondition: the value was on the request that passed through the client's logging handler.
        Assert.Equal(_authorizationValue, Assert.Single(_sentAuthorization ?? []));
        // Precondition: the client's logger wrote this send, so the absence below is not an unwired provider.
        Assert.Contains(lines, line => line.Category == ClientLoggerCategory);
        Assert.DoesNotContain(lines, line => line.Category == DefaultLogicalHandlerCategory);
        Assert.All(lines, line => Assert.DoesNotContain(_authorizationValue, line.Text, StringComparison.Ordinal));
    }

    private sealed record CapturedLine(string Category, string Text);

    /// <summary>Records every log call and scope with its formatted message, its serialized state and its exception text.</summary>
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

        private static string Serialize(object? state)
        {
            var text = new StringBuilder();
            Expand(state, text, depth: 0);
            return text.ToString();
        }

        // Deeper than any state the logging handlers build; a cyclic enumerable reaches it and fails the test.
        private const int MaxDepth = 16;

        /// <summary>
        /// Writes a value's contents rather than its text: every pair's key and value, every element of a collection,
        /// recursively, so an array of header values prints its strings, not its type name. Any other leaf is rendered
        /// through its <c>ToString()</c>, which cannot see a value held only in one of its properties. Nesting deeper
        /// than <see cref="MaxDepth"/> fails the test rather than truncating the text.
        /// </summary>
        private static void Expand(object? value, StringBuilder into, int depth)
        {
            if (depth > MaxDepth)
                Assert.Fail($"Log state nests deeper than {MaxDepth} levels; a cyclic value cannot be fully serialized.");

            switch (value)
            {
                case null:
                    return;
                case string text:
                    into.Append(text).Append(' ');
                    return;
                case IEnumerable items:
                    foreach (var item in items)
                        Expand(item, into, depth + 1);
                    return;
            }

            var type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Expand(type.GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetValue(value), into, depth + 1);
                Expand(type.GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetValue(value), into, depth + 1);
                return;
            }

            into.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(' ');
        }

        private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Add(category, $"{Convert.ToString(state, CultureInfo.InvariantCulture)} {Serialize(state)}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add(category, $"{formatter(state, exception)} {Serialize(state)} {exception}");
        }
    }
}
