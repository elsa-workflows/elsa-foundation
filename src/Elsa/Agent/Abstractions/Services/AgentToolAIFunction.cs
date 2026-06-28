using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Core.Services;

/// <summary>
/// The context an <see cref="IAgentTool"/> needs to run when invoked as a Microsoft.Extensions.AI
/// <see cref="AIFunction"/> — used both when a harness (e.g. GitHub Copilot) auto-invokes our tools and
/// when a model provider drives function-calling itself.
/// </summary>
public sealed record AgentToolAIFunctionContext(
    string SessionId,
    string ActorId,
    IReadOnlyCollection<AgentContextAttachment> Context,
    AgentPolicy Policy,
    IAgentToolInvoker Invoker,
    Action<AgentActionProposal>? OnProposal = null);

/// <summary>Projects <see cref="IAgentTool"/>s to MEAI <see cref="AIFunction"/>s — the shared tool currency across providers.</summary>
public static class AgentToolAIFunctions
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    public static AIFunction Create(IAgentTool tool, AgentToolAIFunctionContext context)
        => new AgentToolAIFunction(tool.Descriptor, context);

    public static IReadOnlyList<AIFunction> CreateAll(IEnumerable<IAgentTool> tools, AgentToolAIFunctionContext context)
        => tools.Select(tool => Create(tool, context)).ToList();

    internal static JsonElement ParseSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return EmptyObjectSchema;

        try
        {
            return JsonDocument.Parse(schema).RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObjectSchema;
        }
    }
}

internal sealed class AgentToolAIFunction(AgentToolDescriptor descriptor, AgentToolAIFunctionContext context) : AIFunction
{
    public override string Name => descriptor.Name;

    public override string Description => descriptor.Description;

    public override JsonElement JsonSchema { get; } = AgentToolAIFunctions.ParseSchema(descriptor.JsonSchema);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var args = arguments.ToDictionary(pair => pair.Key, pair => pair.Value);
        var invocation = new AgentToolInvocation(Guid.NewGuid().ToString("N"), descriptor.Name, context.SessionId, context.ActorId, args, context.Context);
        var dispatch = await context.Invoker.DispatchAsync(invocation, context.Policy, cancellationToken);

        return dispatch.Outcome switch
        {
            AgentToolDispatchOutcome.Executed => dispatch.Result?.Content ?? string.Empty,
            AgentToolDispatchOutcome.ProposalCreated => HandleProposal(dispatch.Proposal!),
            _ => $"Tool '{descriptor.Name}' was denied: {dispatch.Error?.Message}"
        };
    }

    private string HandleProposal(AgentActionProposal proposal)
    {
        context.OnProposal?.Invoke(proposal);
        return $"A change proposal '{proposal.Id}' was created and is awaiting human approval. It has NOT been applied yet.";
    }
}
