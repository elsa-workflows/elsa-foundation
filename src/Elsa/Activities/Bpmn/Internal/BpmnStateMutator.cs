using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// The single home for immutable <see cref="BpmnExecutionState"/> mutations. Every builder bumps
/// <see cref="BpmnExecutionState.Sequence"/> and <see cref="NewId"/> derives every record id
/// (<c>token:N</c>, <c>diag:N</c>) from that sequence, so the persisted id stream is a pure, stable
/// function of mutation order and count (the same discipline as <c>FlowchartStateMutator</c>).
/// </summary>
internal static class BpmnStateMutator
{
    public static string NewId(BpmnExecutionState state, string prefix) =>
        $"{prefix}:{state.Sequence + 1}";

    public static BpmnToken NewToken(
        BpmnExecutionState state,
        string atElementId,
        string? flowId,
        string? parentTokenId,
        BpmnTokenStatus status,
        string? producingActivityExecutionId) =>
        new(NewId(state, "token"), atElementId, flowId, parentTokenId, status, producingActivityExecutionId);

    public static BpmnExecutionState AddToken(BpmnExecutionState state, BpmnToken token) =>
        state with { Tokens = state.Tokens.Append(token).ToArray(), Sequence = state.Sequence + 1 };

    public static BpmnExecutionState UpdateToken(BpmnExecutionState state, BpmnToken token) =>
        state with
        {
            Tokens = state.Tokens
                .Select(existing => StringComparer.Ordinal.Equals(existing.TokenId, token.TokenId) ? token : existing)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static BpmnExecutionState AddActiveChild(BpmnExecutionState state, BpmnActiveChild child) =>
        state with { ActiveChildren = state.ActiveChildren.Append(child).ToArray(), Sequence = state.Sequence + 1 };

    public static BpmnExecutionState RemoveActiveChild(BpmnExecutionState state, string tokenId) =>
        state with
        {
            ActiveChildren = state.ActiveChildren
                .Where(child => !StringComparer.Ordinal.Equals(child.TokenId, tokenId))
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static BpmnExecutionState AddRace(BpmnExecutionState state, string gatewayElementId, IReadOnlyCollection<string> memberTokenIds)
    {
        var race = new BpmnEventRace(NewId(state, "race"), gatewayElementId, memberTokenIds);
        return state with { Races = state.Races.Append(race).ToArray(), Sequence = state.Sequence + 1 };
    }

    public static BpmnExecutionState MarkRaceResolved(BpmnExecutionState state, string raceId) =>
        state with
        {
            Races = state.Races
                .Select(race => StringComparer.Ordinal.Equals(race.RaceId, raceId) ? race with { Resolved = true } : race)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static (BpmnExecutionState State, BpmnLoopState Loop) AddLoop(
        BpmnExecutionState state,
        string coordinatorTokenId,
        string elementId,
        bool isSequential,
        int totalCount)
    {
        var loop = new BpmnLoopState(NewId(state, "loop"), coordinatorTokenId, elementId, isSequential, totalCount, nextIndex: 0, completedCount: 0);
        return (state with { Loops = state.Loops.Append(loop).ToArray(), Sequence = state.Sequence + 1 }, loop);
    }

    public static BpmnExecutionState UpdateLoop(BpmnExecutionState state, BpmnLoopState loop) =>
        state with
        {
            Loops = state.Loops
                .Select(existing => StringComparer.Ordinal.Equals(existing.LoopId, loop.LoopId) ? loop : existing)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static BpmnExecutionState RemoveLoop(BpmnExecutionState state, string loopId) =>
        state with
        {
            Loops = state.Loops
                .Where(loop => !StringComparer.Ordinal.Equals(loop.LoopId, loopId))
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    /// <summary>The live multi-instance loop coordinated by <paramref name="coordinatorTokenId"/>, or <c>null</c> when that token is not a coordinator.</summary>
    public static BpmnLoopState? FindLoopByCoordinator(BpmnExecutionState state, string coordinatorTokenId) =>
        state.Loops.FirstOrDefault(loop => StringComparer.Ordinal.Equals(loop.TokenId, coordinatorTokenId));

    public static BpmnToken GetRequiredToken(BpmnExecutionState state, string tokenId) =>
        state.Tokens.FirstOrDefault(token => StringComparer.Ordinal.Equals(token.TokenId, tokenId))
        ?? throw new Exceptions.BpmnExecutionException($"BPMN token '{tokenId}' was not found on the execution state.");
}
