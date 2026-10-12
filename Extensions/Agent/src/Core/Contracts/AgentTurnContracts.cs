using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>Tunables for the turn orchestrator.</summary>
public sealed class AgentTurnOptions
{
    /// <summary>Maximum number of model/tool steps a single turn may take before it is force-completed.</summary>
    public int MaxSteps { get; set; } = 8;
}

/// <summary>
/// Tracks in-flight turns so they can be cancelled out-of-band (e.g. a Stop button posts to the cancel endpoint).
/// </summary>
public interface IAgentTurnRegistry
{
    /// <summary>Registers a turn and returns a token that is cancelled when <see cref="Cancel"/> is called for it.</summary>
    CancellationToken Register(string turnId);

    /// <summary>Cancels a registered turn. Returns false if the turn id is unknown.</summary>
    bool Cancel(string turnId);

    /// <summary>Removes and disposes a turn's cancellation source.</summary>
    void Unregister(string turnId);
}

/// <summary>State persisted when a turn pauses awaiting approval of a mutating tool, so it can resume after execution.</summary>
public sealed record AgentPausedTurn(
    string SessionId,
    string TurnId,
    string ProposalId,
    string ToolCallId,
    string ToolName,
    IReadOnlyList<AgentTurnMessage> History,
    int NextStepIndex);

/// <summary>Stores paused-turn state keyed by session, enabling resume-after-approval.</summary>
public interface IAgentTurnStateStore
{
    void Save(AgentPausedTurn state);

    AgentPausedTurn? Find(string sessionId);

    void Remove(string sessionId);
}
