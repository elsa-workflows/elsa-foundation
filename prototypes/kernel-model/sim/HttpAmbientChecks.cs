using System.Runtime.CompilerServices;

namespace Kernel.Sim;

// A reference to the actual posted creator and its process epoch, separate from its reusable wire identity.
internal sealed record HttpStarter(string Ambient, int Actor, long Epoch);
internal sealed record HttpAmbientTrace(HttpStarter? Starter, int Observations);

public sealed partial class World
{
    private ConditionalWeakTable<Message, HttpStarter>? _postedHttpStarts;
    private ConditionalWeakTable<HttpState, HttpAmbientTrace>? _stateAmbients;
    private ConditionalWeakTable<InstanceRow, HttpAmbientTrace>? _preparedAmbients;
    private Dictionary<string, AmbientCheckpoint>? _currentAmbients;
    private List<AmbientCheckpoint>? _ambientChecks;

    private sealed class AmbientCheckpoint(string id, long generation, Task<InMemoryStore.DeletedCheckpoint> deleted, HttpAmbientTrace trace)
    {
        public string Id => id;
        public long Generation => generation;
        public Task<InMemoryStore.DeletedCheckpoint> Deleted => deleted;
        public HttpAmbientTrace Trace = trace;
    }

    private void ObserveHttpTurn(Actor actor, ITurn turn, HttpState state)
    {
        HttpAmbientTrace? trace = null;
        if (state.NewInstance)
        {
            HttpStarter? starter = null;
            if (turn.Inbox.FirstOrDefault() is { } creator) _postedHttpStarts?.TryGetValue(creator, out starter);
            trace = new(starter, 0);
        }
        else if (_stateAmbients?.TryGetValue(state, out trace) != true &&
            _currentAmbients?.GetValueOrDefault(turn.Instance.Ref.Id) is { } prior &&
            prior.Generation == turn.Instance.Creation && !prior.Deleted.IsCompleted)
            trace = prior.Trace;    // earlier landed observations survive eviction and owner loss
        if (trace is null) return;
        if (trace.Starter is { } local && local.Actor == actor.Id && local.Epoch == actor.Worker.Epoch &&
            ReferenceEquals(turn.Ambient, local.Ambient))
            trace = trace with { Observations = trace.Observations + 1 };
        (_stateAmbients ??= new()).AddOrUpdate(state, trace);
    }

    private void PrepareCheckpoint(TurnCommit turn, object? modelState, Message? input)
    {
        var row = turn.Row;
        var lifecycle = input is not null && (turn.Delete || row is { Creation: > 0, Revision: 1 } ||
            turn.Expected is null && row is { Creation: 0, Revision: > 0 } && (row.Model == "http" || _physicalCheckpoints?.ContainsKey(turn.Instance) == true || _sampledActive?.ContainsKey(turn.Instance) == true));
        if (lifecycle) Store.Lifecycle!.Watch(turn.Instance);
        if (row is { Model: "http", Retired: false } && modelState is HttpState http && _stateAmbients?.TryGetValue(http, out var trace) == true)
            (_preparedAmbients ??= new()).GetValue(row, _ => trace);
    }

    // The proposed row is the very object the fenced store operation used. An unexecuted call, losing candidate,
    // lost fence or same-valued row cannot install its trace. A lost acknowledgement still leaves a landed birth.
    internal void ObserveAmbientCommit(CommitBatch batch)
    {
        if (_preparedAmbients is null) return;
        foreach (var turn in batch.Turns)
        {
            if (turn.Row is not { } row || !_preparedAmbients.TryGetValue(row, out var trace)) continue;
            if (_currentAmbients?.GetValueOrDefault(row.Id) is { } current && !current.Deleted.IsCompleted)
                current.Trace = trace;
            else
            {
                var installed = new AmbientCheckpoint(row.Id, row.Creation, Store.WatchDeletion(new(row.Id, row.Creation)), trace);
                (_currentAmbients ??= [])[row.Id] = installed;
                (_ambientChecks ??= []).Add(installed);
            }
        }
    }

    private string? CheckHttpAmbients()
    {
        if (_ambientChecks is not null)
            for (var i = _ambientChecks.Count - 1; i >= 0; i--)
            {
                var capture = _ambientChecks[i];
                var retired = capture.Deleted.IsCompletedSuccessfully;
                var row = retired ? capture.Deleted.Result.Row : Store.Instances.GetValueOrDefault(capture.Id);
                if (row is { Model: "http", Retired: false, State: not "null" } && AmbientError(row, capture.Trace) is { } error) return error;
                if (!retired) continue;
                _ambientChecks.RemoveAt(i);
                if (_currentAmbients is { } active && active.GetValueOrDefault(capture.Id) == capture) active.Remove(capture.Id);
            }
        // A named HTTP checkpoint with no witnessed local creator still has no issued ambient evidence.
        foreach (var id in Oracle.Https.Keys)
            if (_currentAmbients?.ContainsKey(id) != true && Store.Instances.GetValueOrDefault(id) is { Model: "http", Retired: false, State: not "null" } row &&
                AmbientError(row, null) is { } error) return error;
        return null;
    }

    private string? AmbientError(InstanceRow row, HttpAmbientTrace? trace)
    {
        var state = ParsedState<HttpState>(row.State);
        var expected = trace?.Starter is { } local ? $"seen@{local.Actor}" : null;
        for (var i = 0; i < state.Ambient.Count; i++)
            if (i >= (trace?.Observations ?? 0) || state.Ambient[i] != expected)
                return $"I15 {row.Id} generation {row.Creation} observed ambient {state.Ambient[i]} outside its starting activation";
        return null;
    }
}
