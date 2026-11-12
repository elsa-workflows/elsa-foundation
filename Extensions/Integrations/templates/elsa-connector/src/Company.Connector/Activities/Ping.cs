using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Company.Connector.Activities;

[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class Ping : ConnectorActivity<string>
{
    public Ping(IHttpClientFactory httpClientFactory, IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }

    [ActivityInput(Key = nameof(Connection), DisplayName = "Connection", Tier = InputTier.Essential)]
    [ActivityConnection(ConnectionType = "http")]
    public ConnectionReference? Connection { get; set; }

    [ActivityInput(Tier = InputTier.Essential)]
    public string Url { get; set; } = "https://example.com/health";

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        await ApplyConnectionAsync(request, Connection, context, context.CancellationToken);
        using var response = await CreateHttpClient().SendAsync(request, context.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
        return ActivityTransition.Complete(body);
    }
}
