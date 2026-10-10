using System.Net.Http;
using Elsa.Activities.Http.Constants;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Http.Services;

/// <summary>
/// The one logger of the named <see cref="HttpActivityConstants.HttpClientName"/> client, which replaces the
/// factory's default logging handlers (spec 188 FR-019, research R17). It writes the request lines those handlers
/// wrote, minus every header: the method and the URI on start, the status code and elapsed time on a response, and the
/// elapsed time and exception type name on a failure. It never logs a header, a header value, an exception message or
/// an exception object, so a header value enters neither its message text nor its structured state.
/// </summary>
/// <remarks>
/// It logs under the category the default client handler used (<see cref="CategoryName"/>), with that handler's event
/// ids, so a host's existing level filters still apply. <c>RequestStart</c> and <c>RequestEnd</c> keep that handler's
/// message templates; <c>RequestFailed</c> does not: the default writes "HTTP request failed after
/// {ElapsedMilliseconds}ms" with the exception object, and this one appends " - {ExceptionType}" and passes no
/// exception. The URI is redacted the way <c>Microsoft.Extensions.Http</c> 10 redacts it by default: scheme, host, port
/// when not the default, and path, with a query replaced by <c>*</c>; user info and fragment are never logged.
/// The redaction is a copy of that package's internal <c>UriRedactionHelper</c> (framework section 2.17: the helper is
/// internal, so a dependency on it is not possible), and the package's switch that disables its redaction is not
/// honored, by design: this logger exists so a raw query is never logged.
/// </remarks>
public sealed class HttpActivityClientLogger(ILoggerFactory loggerFactory) : IHttpClientLogger
{
    /// <summary>The default client handler's category for the named client.</summary>
    public const string CategoryName = "System.Net.Http.HttpClient." + HttpActivityConstants.HttpClientName + ".ClientHandler";

    private static readonly Action<ILogger, HttpMethod, string, Exception?> RequestStart = LoggerMessage.Define<HttpMethod, string>(
        LogLevel.Information, new EventId(100, "RequestStart"), "Sending HTTP request {HttpMethod} {Uri}");

    private static readonly Action<ILogger, double, int, Exception?> RequestEnd = LoggerMessage.Define<double, int>(
        LogLevel.Information, new EventId(101, "RequestEnd"), "Received HTTP response headers after {ElapsedMilliseconds}ms - {StatusCode}");

    private static readonly Action<ILogger, double, string, Exception?> RequestFailed = LoggerMessage.Define<double, string>(
        LogLevel.Information, new EventId(104, "RequestFailed"), "HTTP request failed after {ElapsedMilliseconds}ms - {ExceptionType}");

    private readonly ILogger _logger = loggerFactory.CreateLogger(CategoryName);

    /// <inheritdoc />
    public object? LogRequestStart(HttpRequestMessage request)
    {
        RequestStart(_logger, request.Method, RedactedUri(request.RequestUri), null);
        return null;
    }

    /// <inheritdoc />
    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed) =>
        RequestEnd(_logger, elapsed.TotalMilliseconds, (int)response.StatusCode, null);

    // The exception's type only: its message can carry request details, and the exception object would carry it.
    /// <inheritdoc />
    public void LogRequestFailed(object? context, HttpRequestMessage request, HttpResponseMessage? response, Exception exception, TimeSpan elapsed) =>
        RequestFailed(_logger, elapsed.TotalMilliseconds, exception.GetType().Name, null);

    private static string RedactedUri(Uri? uri)
    {
        if (uri is null)
            return string.Empty;
        if (!uri.IsAbsoluteUri)
            return "*";

        var authority = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return uri.Query.Length > 1 ? $"{authority}{uri.AbsolutePath}?*" : $"{authority}{uri.AbsolutePath}";
    }
}
