using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests.Support;

[Activity(Icon = "plug")]
[Version("1.2.3")]
public sealed class SamplePingActivity : ConnectorActivity<string>
{
    public SamplePingActivity(IHttpClientFactory httpClientFactory, IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }

    [ActivityInput(Tier = InputTier.Optional, Description = "Absolute URL")]
    public string Url { get; set; } = "https://example.com/health";

    [ActivityInput(Key = nameof(Connection), DisplayName = "Connection", Tier = InputTier.Essential)]
    [ActivityConnection(ConnectionType = "http")]
    public ConnectionReference? Connection { get; set; }

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        await ApplyConnectionAsync(request, Connection, context, context.CancellationToken);
        using var response = await CreateHttpClient().SendAsync(request, context.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
        return ActivityTransition.Complete(body);
    }

    public ValueTask<ActivityTransition<string>> ExecuteForTestsAsync(ActivityExecutionContext context) =>
        ExecuteAsync(context);
}

[Activity(Icon = "webhook")]
[TriggerActivity]
[Version("2.0.0")]
public sealed class SampleWebhookTrigger : ConnectorActivity
{
    [ActivityInput(Tier = InputTier.Setting)]
    public string SecretHeader { get; set; } = "X-Hook-Secret";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

[Version("9.8.7")]
public abstract class VersionedConnectorBase : ConnectorActivity;

[Activity(Icon = "box")]
public sealed class DerivedFromVersionedBase : VersionedConnectorBase
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

[Activity(Icon = "box")]
public sealed class OpenGenericConnectorActivity<TPayload> : ConnectorActivity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

[Activity(Icon = "plug")]
public abstract class IconOnBaseConnector : ConnectorActivity;

[Activity]
[Version("1.0.0")]
public sealed class DerivedWithoutOwnIcon : IconOnBaseConnector
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

[Activity(Icon = "key")]
[Version("1.0.0")]
public sealed class SampleCredentialActivity : ConnectorActivity
{
    [ActivityInput(IsCredential = true)]
    public string? Token { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
