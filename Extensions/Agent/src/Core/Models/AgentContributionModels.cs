using System.Text.Json;

namespace Elsa.Agent.Core.Models;

/// <summary>A module-owned resource identity. The module interprets its revision and collection targets.</summary>
public sealed record AgentResourceTarget(string ModuleId, string ResourceType, string? ResourceId, string? Revision)
{
    public string? DisplayName { get; init; }
    public string? Route { get; init; }
}

/// <summary>A module-defined JSON payload. Shared orchestration and providers preserve its meaning opaquely.</summary>
public sealed record AgentPayload(string TypeId, string SchemaVersion, JsonElement Data);

/// <summary>One complete context/result disclosure whose originals remain dependencies of a provider transcript.</summary>
/// <remarks>Snapshots preserve grouping and distinct versions of the same attachment identity. They do not authorize execution.</remarks>
public sealed record AgentDisclosureSnapshot(
    IReadOnlyCollection<AgentContextAttachment> Context,
    IReadOnlyList<AgentToolResult> ToolResults);

/// <summary>Caller attribution retained through admission; these fields never grant tool authority.</summary>
public sealed record AgentPromptSource(string? ModuleId, string? ActionId, string? Label);

public enum AgentContextOrigin
{
    ClientSnapshot,
    ResourceData,
    AuthoritativeRead,
    InstalledGuidance
}

public enum AgentExecutionLocation
{
    Server,
    ClientWorkingState
}

public enum AgentEffectScope
{
    ClientWorkingState,
    ServerPersisted
}

public enum AgentClientOutcome
{
    Applied,
    Refused,
    Uncertain
}

/// <summary>Identifies one originating browser registry and module/editor lifetime; it grants no authority.</summary>
public sealed record AgentClientBinding(string ClientId, string GenerationId);

/// <summary>Module validation and review data, before the engine binds server-owned execution identity.</summary>
public sealed record AgentToolPreparation(
    AgentResourceTarget? Target,
    JsonElement Arguments,
    AgentRisk Risk,
    AgentPayload Preview,
    bool RequiresApproval)
{
    public AgentClientBinding? Client { get; init; }
}

/// <summary>The frozen, server-retained meaning of one validated tool invocation and its review.</summary>
public sealed record AgentPreparedToolInvocation(
    string InvocationId,
    string SessionId,
    string TurnId,
    string ToolCallId,
    string ToolName,
    string ToolContractVersion,
    string ModuleId,
    string ActorId,
    string TenantId,
    AgentResourceTarget? Target,
    JsonElement Arguments,
    string ApprovalDigest,
    AgentRisk Risk,
    AgentPayload Preview,
    AgentExecutionLocation ExecutionLocation,
    bool RequiresApproval,
    AgentClientBinding? Client,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Original module-declared result disclosure owner, frozen into preparation and its review digest.</summary>
    public string? DisclosureSourceId { get; init; }
}

/// <summary>An owning module's observed effect; local working state does not imply durable save/publish.</summary>
public sealed record AgentEffectReceipt(
    string InvocationId,
    AgentResourceTarget Before,
    AgentResourceTarget After,
    AgentEffectScope Scope,
    AgentPayload Evidence);

/// <summary>A browser observation admitted by the owning backend tool; acknowledging never executes a tool.</summary>
public sealed record AgentClientAcknowledgement(
    AgentClientBinding Client,
    string ApprovalDigest,
    AgentClientOutcome Outcome,
    string Summary,
    AgentResourceTarget? After,
    AgentPayload? Result);

/// <summary>Installed context source identity and the client schemas it can admit.</summary>
public sealed record AgentContextProviderDescriptor(
    string Id,
    string ModuleId,
    string ScopeKind,
    IReadOnlyCollection<string> ContextKinds,
    IReadOnlyCollection<string> SchemaVersions,
    IReadOnlyCollection<string> ResourceTypes)
{
    /// <summary>Whether a missing, failed or timed-out source blocks provider use. Existing sources remain required by default.</summary>
    public bool Required { get; init; } = true;
}

/// <summary>Source limitations are data. Required outcomes block collection; unexpected required-source faults retain Sequential failure semantics.</summary>
public sealed record AgentContributionDiagnostic(string ContributorId, string Code, string Message, bool Required);

public sealed record AgentContextContribution(
    IReadOnlyCollection<AgentContextAttachment> Attachments,
    IReadOnlyCollection<AgentContributionDiagnostic> Diagnostics);

/// <summary>Invalid installed contribution composition, a contribution contract violation, or failure to establish retained disclosure provenance.</summary>
/// <remarks>Infrastructure failures preserve the original cause as <see cref="Exception.InnerException"/>; transport callers redact its details.</remarks>
public sealed class AgentContributionException(string message, Exception? innerException = null) : Exception(message, innerException);
