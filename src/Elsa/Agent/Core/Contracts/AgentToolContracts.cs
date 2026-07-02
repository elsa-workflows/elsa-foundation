using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>Whether a tool only reads state or mutates it. Mutating tools are policy-gated.</summary>
public enum AgentToolMutability
{
    ReadOnly,
    Mutating
}

/// <summary>The outcome of dispatching a tool invocation through the policy-aware invoker.</summary>
public enum AgentToolDispatchOutcome
{
    Executed,
    ProposalCreated,
    Denied
}

/// <summary>Describes a server-side tool that the agent can call. The descriptor is what the model sees.</summary>
public sealed record AgentToolDescriptor(
    string Name,
    string DisplayName,
    string Description,
    AgentToolMutability Mutability,
    AgentRisk Risk,
    string JsonSchema,
    IReadOnlyCollection<string> RequiredPermissions,
    IReadOnlyCollection<string> AgentScopes);

/// <summary>A concrete request to run a tool, produced by the orchestrator from a provider tool call.</summary>
public sealed record AgentToolInvocation(
    string ToolCallId,
    string ToolName,
    string SessionId,
    string ActorId,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyCollection<AgentContextAttachment> Context);

/// <summary>The result of <see cref="IAgentToolInvoker.DispatchAsync"/>.</summary>
public sealed record AgentToolDispatch(
    AgentToolDispatchOutcome Outcome,
    AgentToolResult? Result,
    AgentActionProposal? Proposal,
    AgentError? Error)
{
    public static AgentToolDispatch Executed(AgentToolResult result) => new(AgentToolDispatchOutcome.Executed, result, null, null);

    public static AgentToolDispatch Proposed(AgentActionProposal proposal) => new(AgentToolDispatchOutcome.ProposalCreated, null, proposal, null);

    public static AgentToolDispatch Denied(AgentError error) => new(AgentToolDispatchOutcome.Denied, null, null, error);
}

/// <summary>A server-side tool the agent can invoke during a turn.</summary>
public interface IAgentTool
{
    AgentToolDescriptor Descriptor { get; }

    Task<AgentResult<AgentToolResult>> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default);
}

/// <summary>Catalog of registered tools.</summary>
public interface IAgentToolRegistry
{
    IReadOnlyCollection<AgentToolDescriptor> Descriptors { get; }

    IReadOnlyCollection<IAgentTool> Tools { get; }

    IAgentTool? Find(string toolName);
}

/// <summary>
/// Applies policy to a tool invocation: read-only tools run inline; mutating tools become proposals
/// (or run inline under a full-auto policy).
/// </summary>
public interface IAgentToolInvoker
{
    Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default);
}
