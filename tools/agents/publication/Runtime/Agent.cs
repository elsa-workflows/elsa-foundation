using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Acme.Agents.Runtime;

// Research package contracts, not proposed Elsa product APIs.
public sealed record AgentDescriptor(string AgentVersionId, string Instructions, string ModelId, JsonElement OutputSchema);

public sealed class AgentActivationStrategy(IChatClient client) : IActivityActivationStrategy
{
    public const string Key = "acme.agent";
    public string ConsumerKey => Key;
    public IReadOnlyCollection<string> SupportedSchemaVersions => ["1"];
    public bool RequiresInputHydration => true;

    public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Descriptor.ConsumerKey != Key || request.Descriptor.SchemaVersion != "1")
            throw new InvalidOperationException("Unsupported agent descriptor");
        var descriptor = request.Descriptor.Payload.Deserialize<AgentDescriptor>()!;
        return ValueTask.FromResult(new ActivityActivationLease(new AgentActivity(client, descriptor)));
    }
}

internal sealed class AgentActivity(IChatClient client, AgentDescriptor descriptor) : IActivity
{
    [ActivityInput] public string Brief { get; set; } = "";

    public async ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context)
    {
        var agent = new ChatClientAgent(client, instructions: descriptor.Instructions, name: descriptor.AgentVersionId);
        var options = new ChatClientAgentRunOptions(new ChatOptions
        {
            ModelId = descriptor.ModelId,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(descriptor.OutputSchema, "article")
        });
        var result = await agent.RunAsync(Brief, options: options, cancellationToken: context.CancellationToken);
        // The research contract is deliberately exact and small; general JSON-schema validation is M1.
        using var document = JsonDocument.Parse(result.Text);
        var output = document.RootElement;
        if (output.ValueKind != JsonValueKind.Object || output.EnumerateObject().Count() != 1
            || !output.TryGetProperty("article", out var article) || article.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("agent.output.invalid");
        return ActivityTransition.Complete(output.Clone());
    }
}
