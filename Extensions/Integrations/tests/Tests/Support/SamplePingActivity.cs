using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests.Support;

[Activity(Icon = "plug")]
[Version("1.2.3")]
public sealed class SamplePingActivity : ConnectorActivity<string>
{
    public SamplePingActivity(IConnectionContext connectionContext) : base(connectionContext)
    {
    }

    [ActivityInput(Tier = InputTier.Essential)]
    public ConnectionReference Connection { get; set; } = new("demo");

    [ActivityInput(Tier = InputTier.Optional, Description = "Relative path")]
    public string Path { get; set; } = "/health";

    [ActivityInput(IsCredential = true)]
    public string? ApiKey { get; set; }

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        await using var handle = await ConnectionContext.ResolveAsync(Connection, context.CancellationToken);
        var body = await handle.HttpClient.GetStringAsync(Path, context.CancellationToken);
        return ActivityTransition.Complete(body);
    }
}

[Activity(Icon = "webhook")]
[TriggerActivity]
[Version("2.0.0")]
public sealed class SampleWebhookTrigger : ConnectorActivity
{
    public SampleWebhookTrigger(IConnectionContext connectionContext) : base(connectionContext)
    {
    }

    [ActivityInput(Tier = InputTier.Setting)]
    public string SecretHeader { get; set; } = "X-Hook-Secret";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}
