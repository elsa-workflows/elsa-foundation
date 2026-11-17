using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Microsoft.Extensions.AI;

namespace Elsa.Weaver.Core.Services;

/// <summary>
/// The context an <see cref="IWeaverTool"/> needs to run when invoked as a Microsoft.Extensions.AI
/// <see cref="AIFunction"/> — used both when a harness (e.g. GitHub Copilot) auto-invokes our tools and
/// when a model provider drives function-calling itself.
/// </summary>
public sealed record WeaverToolAIFunctionContext(
    string SessionId,
    string ActorId,
    IReadOnlyCollection<WeaverContextAttachment> Context,
    WeaverPolicy Policy,
    IWeaverToolInvoker Invoker,
    Action<WeaverActionProposal>? OnProposal = null)
{
    public string TenantId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public WeaverClientBinding? Client { get; init; }
    public bool AllowMutations { get; init; } = true;
    public Action<WeaverToolResult>? OnResult { get; init; }
}

/// <summary>Projects <see cref="IWeaverTool"/>s to MEAI <see cref="AIFunction"/>s — the shared tool currency across providers.</summary>
public static class WeaverToolAIFunctions
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    public static AIFunction Create(IWeaverTool tool, WeaverToolAIFunctionContext context)
        => new WeaverToolAIFunction(tool.Descriptor, context);

    public static IReadOnlyList<AIFunction> CreateAll(IEnumerable<IWeaverTool> tools, WeaverToolAIFunctionContext context)
        => tools.Select(tool => Create(tool, context)).ToList();

    /// <summary>
    /// Projects tool descriptors to <b>declaration-only</b> <see cref="AIFunction"/>s — Name/Description/JsonSchema
    /// with no executable body. Used by orchestrator-driven providers (e.g. the Anthropic provider) that advertise
    /// tools so the model emits <c>FunctionCallContent</c>, while the host orchestrator owns dispatch through the
    /// policy-aware invoker. Invoking one directly is a contract violation and throws.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateDeclarations(IEnumerable<WeaverToolDescriptor> descriptors)
        => descriptors.Select(descriptor => (AIFunction)new WeaverToolDeclarationAIFunction(descriptor)).ToList();

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

internal sealed class WeaverToolAIFunction(WeaverToolDescriptor descriptor, WeaverToolAIFunctionContext context) : AIFunction
{
    public override string Name => descriptor.Name;

    public override string Description => descriptor.Description;

    public override JsonElement JsonSchema { get; } = WeaverToolAIFunctions.ParseSchema(descriptor.JsonSchema);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!context.AllowMutations && descriptor.Mutability == WeaverToolMutability.Mutating)
            return new { status = "denied", executed = false, error = "Mutation tools are unavailable during receipt continuation." };
        var args = arguments.ToDictionary(pair => pair.Key, pair => pair.Value);
        var invocation = new WeaverToolInvocation(WeaverProviderPrimitives.NewId(), descriptor.Name, context.SessionId, context.ActorId, args, context.Context)
        { TenantId = context.TenantId, TurnId = context.TurnId, Client = context.Client };
        var dispatch = await context.Invoker.DispatchAsync(invocation, context.Policy, cancellationToken);

        return dispatch.Outcome switch
        {
            WeaverToolDispatchOutcome.Executed => HandleResult(dispatch.Result),
            WeaverToolDispatchOutcome.ProposalCreated => HandleProposal(dispatch.Proposal!),
            _ => new { status = "denied", executed = false, error = dispatch.Error }
        };
    }

    private object? HandleResult(WeaverToolResult? result)
    {
        if (result is not null) context.OnResult?.Invoke(result);
        return result;
    }

    private object HandleProposal(WeaverActionProposal proposal)
    {
        context.OnProposal?.Invoke(proposal);
        return new { status = "awaiting-approval", executed = false, proposalId = proposal.Id,
            summary = "A reviewable proposal is awaiting human approval. No change has been applied.",
            preview = proposal.PreparedInvocation?.Preview };
    }
}

/// <summary>A tool advertised to a model for function-calling but never executed here — the orchestrator runs it.</summary>
internal sealed class WeaverToolDeclarationAIFunction(WeaverToolDescriptor descriptor) : AIFunction
{
    public override string Name => descriptor.Name;

    public override string Description => descriptor.Description;

    public override JsonElement JsonSchema { get; } = WeaverToolAIFunctions.ParseSchema(descriptor.JsonSchema);

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        => throw new NotSupportedException($"Tool '{descriptor.Name}' is declaration-only; the Weaver orchestrator dispatches it through the policy-aware invoker.");
}
