using System.Net.Http.Headers;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Integrations.Tests.Support;

internal sealed class RecordingHttpConnectionApplicator : IHttpConnectionApplicator
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public ConnectionReference? LastConnection { get; private set; }

    public AuthenticationHeaderValue? Authorization { get; set; }

    public ValueTask ApplyAsync(
        HttpRequestMessage request,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LastRequest = request;
        LastConnection = connection;
        if (Authorization is not null)
            request.Headers.Authorization = Authorization;
        return ValueTask.CompletedTask;
    }
}
