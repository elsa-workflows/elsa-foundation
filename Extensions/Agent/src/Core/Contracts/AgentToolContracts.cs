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

/// <summary>An installed module's trusted tool contract, including its supported execution location.</summary>
public sealed record AgentToolDescriptor(
    string Name,
    string DisplayName,
    string Description,
    AgentToolMutability Mutability,
    AgentRisk Risk,
    string JsonSchema,
    IReadOnlyCollection<string> RequiredPermissions,
    IReadOnlyCollection<string> AgentScopes)
{
    public string ModuleId { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = "1";
    /// <summary>Installed context source owning disclosure of this tool's results. Missing support fails closed before provider use.</summary>
    public string? DisclosureSourceId { get; init; }
    public AgentExecutionLocation ExecutionLocation { get; init; } = AgentExecutionLocation.Server;
    public string? ResultType { get; init; }
    public string? ResultSchemaVersion { get; init; }
    public string? ResultJsonSchema { get; init; }
    public string? ResultRendererId { get; init; }
}

/// <summary>A concrete request to run a tool, produced by the orchestrator from a provider tool call.</summary>
public sealed record AgentToolInvocation(
    string ToolCallId,
    string ToolName,
    string SessionId,
    string ActorId,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyCollection<AgentContextAttachment> Context)
{
    public string TurnId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public AgentClientBinding? Client { get; init; }
    public bool AllowMutations { get; init; } = true;
}

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

    Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default);

    Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default);
}

/// <summary>Optional owner-authority companion on the same installed tool; it adds domain checks to host authority.</summary>
/// <remarks>Invoked for advertisement and concrete execution, including reviewed operations.
/// A null target is an availability hint; a concrete preparation is checked again. Register only the tool, not a second authority registry.</remarks>
public interface IAgentToolAuthorization
{
    ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target,
        CancellationToken cancellationToken = default);
}

/// <summary>Catalog of registered tools.</summary>
public interface IAgentToolRegistry
{
    Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default);

    Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies policy to a tool invocation: read-only tools run inline; mutating tools become proposals
/// (or run inline under a full-auto policy).
/// </summary>
public interface IAgentToolInvoker
{
    Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default);
}
