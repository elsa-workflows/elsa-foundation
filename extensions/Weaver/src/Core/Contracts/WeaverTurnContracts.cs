using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Core.Contracts;

/// <summary>Tunables for the turn orchestrator.</summary>
public sealed class WeaverTurnOptions
{
    /// <summary>Maximum number of model/tool steps a single turn may take before it is force-completed.</summary>
    public int MaxSteps { get; set; } = 8;
}

/// <summary>
/// Tracks in-flight turns so they can be cancelled out-of-band (e.g. a Stop button posts to the cancel endpoint).
/// </summary>
public interface IWeaverTurnRegistry
{
    /// <summary>Registers a turn and returns a token that is cancelled when <see cref="Cancel"/> is called for it.</summary>
    CancellationToken Register(string sessionId, string turnId);

    /// <summary>Cancels a registered turn. Returns false if the turn id is unknown.</summary>
    bool Cancel(string sessionId, string turnId);

    /// <summary>Removes and disposes a turn's cancellation source.</summary>
    void Unregister(string sessionId, string turnId);

    bool IsActive(string sessionId);
}

/// <summary>State persisted when a turn pauses awaiting approval of a mutating tool, so it can resume after execution.</summary>
public sealed record WeaverPausedTurn(
    string SessionId,
    string TurnId,
    string ProposalId,
    string ToolCallId,
    string ToolName,
    IReadOnlyList<WeaverTurnMessage> History,
    int NextStepIndex)
{
    public string SourceMessageId { get; init; } = string.Empty;
    public IReadOnlyCollection<WeaverContextAttachment> Context { get; init; } = [];
    public WeaverClientBinding? Client { get; init; }
    public IReadOnlyList<WeaverToolResult> DisclosureResults { get; init; } = [];
    /// <summary>All transcript disclosures, separately retained from the current execution context/results; null is incomplete legacy state.</summary>
    public IReadOnlyList<WeaverDisclosureSnapshot>? DisclosureHistory { get; init; }
    /// <summary>Canonical checksum of the complete paused transcript/state, excluding this field; detects incomplete storage, not malicious replacement.</summary>
    public string? DisclosureHistoryFingerprint { get; init; }
}

/// <summary>Stores paused-turn state keyed by session, enabling resume-after-approval.</summary>
/// <remarks>
/// The default keeps the paused turn on the default session owner's record, so saving the pause also records the
/// session's Pause claim and the pause survives with the session. A replacement keeps autonomy support, because the
/// orchestrator still claims the pause explicitly, but it cannot recover paused turns: with the in-memory record store the
/// composition reports durable recovery unsupported, and with a durable record store selected it fails shell activation.
/// Only the default orchestrator's continuation releases the Pause claim that saving through the default store takes,
/// so with a replaced orchestrator autonomy is Unsupported in that composition (the session policy participation rule).
/// </remarks>
public interface IWeaverTurnStateStore
{
    /// <summary>Records the session's paused turn, replacing any earlier one.</summary>
    Task SaveAsync(WeaverPausedTurn state, CancellationToken cancellationToken = default);

    /// <summary>The session's paused turn, or null when none is recorded.</summary>
    Task<WeaverPausedTurn?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Removes the session's paused turn; a no-op when none is recorded.</summary>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
