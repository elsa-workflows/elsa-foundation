using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;

namespace Company.Connector.Activities;

[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class Ping : ConnectorActivity<string>
{
    public Ping(IConnectionContext connectionContext) : base(connectionContext)
    {
    }

    [ActivityInput(Tier = InputTier.Essential)]
    public ConnectionReference Connection { get; set; } = new("demo");

    [ActivityInput(Tier = InputTier.Optional)]
    public string Path { get; set; } = "/health";

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        await using var handle = await ConnectionContext.ResolveAsync(Connection, context.CancellationToken);
        var body = await handle.HttpClient.GetStringAsync(Path, context.CancellationToken);
        return ActivityTransition.Complete(body);
    }
}
