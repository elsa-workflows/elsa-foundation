using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Weaver.Core.Models;

/// <summary>A module-owned resource identity. The module interprets its revision and collection targets.</summary>
public sealed record WeaverResourceTarget(string ModuleId, string ResourceType, string? ResourceId, string? Revision)
{
    public string? DisplayName { get; init; }
    public string? Route { get; init; }
}

/// <summary>A module-defined JSON payload. Shared orchestration and providers preserve its meaning opaquely.</summary>
public sealed record WeaverPayload(string TypeId, string SchemaVersion, JsonElement Data);

/// <summary>One complete context/result disclosure whose originals remain dependencies of a provider transcript.</summary>
/// <remarks>Snapshots preserve grouping and distinct versions of the same attachment identity. They do not authorize execution.</remarks>
public sealed record WeaverDisclosureSnapshot(
    IReadOnlyCollection<WeaverContextAttachment> Context,
    IReadOnlyList<WeaverToolResult> ToolResults);

/// <summary>Caller attribution retained through admission; these fields never grant tool authority.</summary>
public sealed record WeaverPromptSource(string? ModuleId, string? ActionId, string? Label);

public enum WeaverContextOrigin
{
    ClientSnapshot,
    ResourceData,
    AuthoritativeRead,
    InstalledGuidance
}

public enum WeaverExecutionLocation
{
    Server,
    ClientWorkingState
}

public enum WeaverEffectScope
{
    ClientWorkingState,
    ServerPersisted,
    /// <summary>The action ran without changing its owning resource's persisted identity or revision.</summary>
    NoPersistedEffect = 2
}

public enum WeaverClientOutcome
{
    Applied,
    Refused,
    Uncertain
}

/// <summary>Identifies one originating browser registry and module/editor lifetime; it grants no authority.</summary>
public sealed record WeaverClientBinding(string ClientId, string GenerationId);

/// <summary>Module validation and review data, before the engine binds server-owned execution identity.</summary>
public sealed record WeaverToolPreparation(
    WeaverResourceTarget? Target,
    JsonElement Arguments,
    WeaverRisk Risk,
    WeaverPayload Preview,
    bool RequiresApproval)
{
    public WeaverClientBinding? Client { get; init; }

    private readonly IReadOnlyList<WeaverPrivilegeClass>? _requiredPrivilegeClasses;

    /// <summary>Additional host privileges; copied on assignment. The engine validates and canonicalizes the set before review.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WeaverPrivilegeClass>? RequiredPrivilegeClasses
    {
        get => _requiredPrivilegeClasses;
        init => _requiredPrivilegeClasses = value is { Count: > 0 } ? Array.AsReadOnly(value.ToArray()) : null;
    }
}

/// <summary>The frozen, server-retained meaning of one validated tool invocation and its review.</summary>
public sealed record WeaverPreparedToolInvocation(
    string InvocationId,
    string SessionId,
    string TurnId,
    string ToolCallId,
    string ToolName,
    string ToolContractVersion,
    string ModuleId,
    string ActorId,
    string TenantId,
    WeaverResourceTarget? Target,
    JsonElement Arguments,
    string ApprovalDigest,
    WeaverRisk Risk,
    WeaverPayload Preview,
    WeaverExecutionLocation ExecutionLocation,
    bool RequiresApproval,
    WeaverClientBinding? Client,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Original module-declared result disclosure owner, frozen into preparation and its review digest.</summary>
    public string? DisclosureSourceId { get; init; }

    private readonly IReadOnlyList<WeaverPrivilegeClass>? _requiredPrivilegeClasses;

    /// <summary>Frozen host privilege requirements. Missing legacy requirements do not change the original approval digest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WeaverPrivilegeClass>? RequiredPrivilegeClasses
    {
        get => _requiredPrivilegeClasses;
        init => _requiredPrivilegeClasses = value is { Count: > 0 } ? Array.AsReadOnly(value.ToArray()) : null;
    }
}

/// <summary>An owning module's observed effect; local working state does not imply durable save/publish.</summary>
public sealed record WeaverEffectReceipt(
    string InvocationId,
    WeaverResourceTarget Before,
    WeaverResourceTarget After,
    WeaverEffectScope Scope,
    WeaverPayload Evidence)
{
    /// <summary>Optional owning-module outcome code, subject to its installed descriptor's declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModuleCode { get; init; }
}

/// <summary>A browser observation admitted by the owning backend tool; acknowledging never executes a tool.</summary>
public sealed record WeaverClientAcknowledgement(
    WeaverClientBinding Client,
    string ApprovalDigest,
    WeaverClientOutcome Outcome,
    string Summary,
    WeaverResourceTarget? After,
    WeaverPayload? Result);

/// <summary>Installed context source identity and the client schemas it can admit.</summary>
public sealed record WeaverContextProviderDescriptor(
    string Id,
    string ModuleId,
    string ScopeKind,
    IReadOnlyCollection<string> ContextKinds,
    IReadOnlyCollection<string> SchemaVersions,
    IReadOnlyCollection<string> ResourceTypes)
{
    /// <summary>Whether a missing, failed or timed-out source blocks provider use. Existing sources remain required by default.</summary>
    public bool Required { get; init; } = true;

    private readonly IReadOnlyCollection<string>? _moduleCodes;

    /// <summary>Copied closed set of typed outcome codes; null preserves undeclared compatibility and empty allows no code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyCollection<string>? ModuleCodes
    {
        get => _moduleCodes;
        init => _moduleCodes = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>Source limitations are data. Required outcomes block collection; unexpected required-source faults retain Sequential failure semantics.</summary>
public sealed record WeaverContributionDiagnostic(string ContributorId, string Code, string Message, bool Required)
{
    /// <summary>
    /// Preview: the owning module could not decide whether this context may be disclosed, as opposed to refusing it. A
    /// required diagnostic is still a refusal (fail closed). When every required diagnostic of a collection says so, the
    /// collection fails as unavailable (503) with the first one's <see cref="Code"/> and a fixed engine message, instead of
    /// <c>weaver.context.incomplete</c>; this diagnostic's <see cref="Message"/> is never forwarded.
    /// </summary>
    /// <remarks>
    /// Honoured only on a diagnostic an installed context source returns under its own descriptor id, which the engine records
    /// as it collects. The engine clears it on any other diagnostic, including one another <c>WeaverContextCollecting</c>
    /// handler adds to the event's collection, and engine (<c>host</c>) diagnostics never carry it. The unavailable outcome
    /// also holds when the owner is asked about a retained lifecycle outcome (denied, refused or uncertain), so a paused
    /// continuation keeps its pause. The code must be the owner's dotted
    /// identifier (ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, at most 128 characters) outside the reserved
    /// <c>weaver.</c> namespace; any other code keeps the ordinary incomplete-context failure. Omitted from the wire when unset.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OwnerUnavailable { get; init; }
}

public sealed record WeaverContextContribution(
    IReadOnlyCollection<WeaverContextAttachment> Attachments,
    IReadOnlyCollection<WeaverContributionDiagnostic> Diagnostics);

/// <summary>Invalid installed contribution composition, a contribution contract violation, or failure to establish retained disclosure provenance.</summary>
/// <remarks>Infrastructure failures preserve the original cause as <see cref="Exception.InnerException"/>; transport callers redact its details.</remarks>
public sealed class WeaverContributionException(string message, Exception? innerException = null) : Exception(message, innerException);
