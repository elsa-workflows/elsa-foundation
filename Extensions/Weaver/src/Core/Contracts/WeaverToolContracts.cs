using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Core.Contracts;

/// <summary>Whether a tool only reads state or mutates it. Mutating tools are policy-gated.</summary>
public enum WeaverToolMutability
{
    ReadOnly,
    Mutating
}

/// <summary>The outcome of dispatching a tool invocation through the policy-aware invoker.</summary>
public enum WeaverToolDispatchOutcome
{
    Executed,
    ProposalCreated,
    Denied
}

/// <summary>An installed module's trusted tool contract, including its supported execution location.</summary>
public sealed record WeaverToolDescriptor(
    string Name,
    string DisplayName,
    string Description,
    WeaverToolMutability Mutability,
    WeaverRisk Risk,
    string JsonSchema,
    IReadOnlyCollection<string> RequiredPermissions,
    IReadOnlyCollection<string> WeaverScopes)
{
    public string ModuleId { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = "1";
    /// <summary>Installed context source owning disclosure of this tool's results. Missing support fails closed before provider use.</summary>
    public string? DisclosureSourceId { get; init; }
    public WeaverExecutionLocation ExecutionLocation { get; init; } = WeaverExecutionLocation.Server;
    public string? ResultType { get; init; }
    public string? ResultSchemaVersion { get; init; }
    public string? ResultJsonSchema { get; init; }
    public string? ResultRendererId { get; init; }
}

/// <summary>A concrete request to run a tool, produced by the orchestrator from a provider tool call.</summary>
public sealed record WeaverToolInvocation(
    string ToolCallId,
    string ToolName,
    string SessionId,
    string ActorId,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyCollection<WeaverContextAttachment> Context)
{
    public string TurnId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public WeaverClientBinding? Client { get; init; }
    public bool AllowMutations { get; init; } = true;
}

/// <summary>The result of <see cref="IWeaverToolInvoker.DispatchAsync"/>.</summary>
public sealed record WeaverToolDispatch(
    WeaverToolDispatchOutcome Outcome,
    WeaverToolResult? Result,
    WeaverActionProposal? Proposal,
    WeaverError? Error)
{
    public static WeaverToolDispatch Executed(WeaverToolResult result) => new(WeaverToolDispatchOutcome.Executed, result, null, null);

    public static WeaverToolDispatch Proposed(WeaverActionProposal proposal) => new(WeaverToolDispatchOutcome.ProposalCreated, null, proposal, null);

    public static WeaverToolDispatch Denied(WeaverError error) => new(WeaverToolDispatchOutcome.Denied, null, null, error);
}

/// <summary>A server-side tool Weaver can invoke during a turn.</summary>
public interface IWeaverTool
{
    WeaverToolDescriptor Descriptor { get; }

    Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default);

    Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default);
}

/// <summary>Optional owner-authority companion on the same installed tool; it adds domain checks to host authority.</summary>
/// <remarks>Invoked for advertisement and concrete execution, including reviewed operations.
/// A null target is an availability hint; a concrete preparation is checked again. Register only the tool, not a second authority registry.</remarks>
public interface IWeaverToolAuthorization
{
    ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
        CancellationToken cancellationToken = default);
}

/// <summary>Catalog of registered tools.</summary>
public interface IWeaverToolRegistry
{
    Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default);

    Task<IWeaverTool?> FindAsync(string toolName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies policy to a tool invocation: read-only tools run inline; mutating tools become proposals
/// (or run inline under a full-auto policy).
/// </summary>
public interface IWeaverToolInvoker
{
    Task<WeaverToolDispatch> DispatchAsync(WeaverToolInvocation invocation, WeaverPolicy policy, CancellationToken cancellationToken = default);
}
