using System.Collections.ObjectModel;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.Models;

/// <summary>
/// Immutable authored request data captured before the exchange. It contains no resolved connection credentials.
/// </summary>
public sealed class HttpActivityRequest
{
    public HttpActivityRequest(Uri url, string method, string? content = null, string? contentType = null,
        IDictionary<string, string>? requestHeaders = null, ConnectionReference? connection = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Url = url;
        Method = method;
        Content = content;
        ContentType = contentType;
        Timeout = timeout;
        RequestHeaders = new ReadOnlyDictionary<string, string>(requestHeaders is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(requestHeaders, StringComparer.Ordinal));
        Connection = connection is null ? null : new ConnectionReference { Name = connection.Name, Type = connection.Type };
    }

    public Uri Url { get; }
    public string Method { get; }
    public string? Content { get; }
    public string? ContentType { get; }
    public TimeSpan? Timeout { get; }
    public IReadOnlyDictionary<string, string> RequestHeaders { get; }
    public ConnectionReference? Connection { get; }
}
