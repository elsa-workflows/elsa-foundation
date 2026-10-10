using System.Globalization;
using System.Net;
using Elsa.Activities.Http.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Spec 188 FR-019, research R17: the lines <see cref="HttpActivityClientLogger"/> itself writes, read through a
/// recording <see cref="ILoggerFactory"/>: the redacted URI, and each line's category, event id, level, rendered text
/// and structured state. Every request and response carries a header whose value no line may hold.
/// </summary>
public sealed class HttpActivityClientLoggerTests
{
    private const string OriginalFormat = "{OriginalFormat}";
    private static readonly TimeSpan Elapsed = TimeSpan.FromMilliseconds(12.5);

    private readonly string _headerValue = Guid.NewGuid().ToString("N");
    private readonly RecordingLoggerFactory _factory = new();
    private readonly HttpActivityClientLogger _logger;

    public HttpActivityClientLoggerTests() => _logger = new HttpActivityClientLogger(_factory);

    public static TheoryData<string?, string> RedactionRows => new()
    {
        { "https://example.test/orders/7", "https://example.test/orders/7" },
        { "https://example.test:8443/orders/7", "https://example.test:8443/orders/7" },
        { "https://example.test/orders/7?page=2&view=full", "https://example.test/orders/7?*" },
        { "https://someone@example.test/orders/7#section", "https://example.test/orders/7" },
        { "https://example.test/p?", "https://example.test/p" },
        { "orders/7?page=2", "*" },
        { null, "" },
    };

    [Theory]
    [MemberData(nameof(RedactionRows))]
    public void RequestStart_LogsTheRedactedUri(string? requestUri, string expected)
    {
        _logger.LogRequestStart(NewRequest(requestUri));

        Assert.Equal(expected, Assert.Single(_factory.Lines).Field("Uri"));
    }

    [Fact]
    public void RequestStart_LogsMethodAndUriOnly()
    {
        _logger.LogRequestStart(NewRequest("https://example.test/orders/7?page=2"));

        AssertSingleLine(100, "RequestStart", "Sending HTTP request GET https://example.test/orders/7?*",
            ("HttpMethod", "GET"), ("Uri", "https://example.test/orders/7?*"));
    }

    [Fact]
    public void RequestStop_LogsElapsedAndStatusCodeOnly()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("X-Probe", _headerValue);

        _logger.LogRequestStop(null, NewRequest("https://example.test/orders/7"), response, Elapsed);

        AssertSingleLine(101, "RequestEnd", "Received HTTP response headers after 12.5ms - 204",
            ("ElapsedMilliseconds", "12.5"), ("StatusCode", "204"));
    }

    [Fact]
    public void RequestFailed_LogsElapsedAndExceptionTypeName_WithoutTheExceptionObject()
    {
        // The exception's message holds the header value, so writing the message or the object would hold it.
        var exception = new HttpRequestException($"refused {_headerValue}");

        _logger.LogRequestFailed(null, NewRequest("https://example.test/orders/7"), null, exception, Elapsed);

        AssertSingleLine(104, "RequestFailed", "HTTP request failed after 12.5ms - HttpRequestException",
            ("ElapsedMilliseconds", "12.5"), ("ExceptionType", nameof(HttpRequestException)));
    }

    private HttpRequestMessage NewRequest(string? requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", _headerValue);
        return request;
    }

    private void AssertSingleLine(int eventId, string eventName, string message, params (string Name, string Value)[] fields)
    {
        var line = Assert.Single(_factory.Lines);
        Assert.Equal(SendHttpRequestTestSupport.ClientLoggerCategory, line.Category);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal(eventId, line.EventId.Id);
        Assert.Equal(eventName, line.EventId.Name);
        Assert.Equal(message, line.Message);
        Assert.Null(line.Exception);
        Assert.Equal(
            fields.Select(field => field.Name).Append(OriginalFormat).Order(StringComparer.Ordinal),
            line.State.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.All(fields, field => Assert.Equal(field.Value, line.Field(field.Name)));
        Assert.DoesNotContain(_headerValue, line.Message, StringComparison.Ordinal);
        Assert.All(line.State, pair => Assert.DoesNotContain(_headerValue, Render(pair.Value), StringComparison.Ordinal));
    }

    private static string Render(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private sealed record RecordedLine(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        Exception? Exception)
    {
        public string Field(string name) => Render(Assert.Single(State, pair => pair.Key == name).Value);
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly List<RecordedLine> _lines = [];

        public IReadOnlyList<RecordedLine> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _lines);

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(string category, List<RecordedLine> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => throw new NotSupportedException("The logger opens no scope.");

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Add(new RecordedLine(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(state).ToArray(),
                exception));
    }
}
