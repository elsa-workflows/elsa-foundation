using System.Runtime.CompilerServices;
using Kernel.Runtime;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    public bool ObserveLifecycles { init { if (value) Lifecycle = new(_gate); } }
    public LifecycleObservation? Lifecycle { get; private set; }

    // The simulator starts following an address at a public opening sample or an instrumented
    // worker's selected lifecycle input. Earlier direct-store history remains an unknown prefix.
    public sealed class LifecycleObservation
    {
        private readonly Lock _gate;
        private readonly HashSet<string> _watched = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<TurnCommit, Prepared> _prepared = new();
        private readonly ConditionalWeakTable<TurnCommit, Message> _cancels = new();
        private List<Change>? _changes;
        internal LifecycleObservation(Lock gate) => _gate = gate;
        public sealed record Change(TurnCommit? Turn, InstanceRow? Before, InstanceRow? After, JournalRow? Receipt,
            Message? Input, bool AppliedCancel, IReadOnlyList<Message>? Refused);
        private sealed record Prepared(Message? Input, List<Delivery>? Refusals);

        public void Watch(string actor) { lock (_gate) _watched.Add(actor); }
        public Func<object?, IReadOnlyList<Delivery>, Message?>? AppliedCancellation { get; set; }

        internal void Prepare(TurnCommit turn, TurnOutcome unit)
        {
            var applied = AppliedCancellation?.Invoke(unit.State ?? unit.Slot?.State, unit.Consumed);
            lock (_gate)
            {
                if (!unit.CreationRefused && !_watched.Contains(turn.Instance)) return;
                if (applied is not null && unit.Consumed.Exists(d => ReferenceEquals(d.Message, applied)))
                    _cancels.AddOrUpdate(turn, applied);
                var input = unit.Delete ? unit.Consumed.Find(d => d.Message.Kind == Kinds.Delete)?.Message :
                    unit.Spec is not null ? unit.Slot?.Starting : unit.Slot?.UnwrittenCreator;
                if (input is not null || unit.CreationRefused)
                    _prepared.AddOrUpdate(turn, new(input, unit.CreationRefused ? unit.Consumed : null));
            }
        }

        internal Change? BeforeTurn(TurnCommit turn, InMemoryStore store)
        {
            _prepared.TryGetValue(turn, out var prepared);
            if (!_watched.Contains(turn.Instance) && prepared?.Refusals is null ||
                turn.Row is null && !turn.Delete && prepared?.Refusals is null) return null;
            bool Disposed(Message m) => (turn.Consumed.Contains(m.InboxKey) || turn.Done.Contains(m.InboxKey)) &&
                store.Messages.TryGetValue(m.InboxKey, out var stored) && ReferenceEquals(stored.Message, m);
            return new(turn, store.Instances.GetValueOrDefault(turn.Instance), null, null, prepared?.Input,
                _cancels.TryGetValue(turn, out var cancel) && Disposed(cancel),
                prepared?.Refusals?.Where(d => !d.Durable || Disposed(d.Message)).Select(d => d.Message).ToArray());
        }

        internal void WriteFreeRefusal(List<Delivery> refused)
        {
            lock (_gate) (_changes ??= []).Add(new(null, null, null, null, null, false, refused.Select(d => d.Message).ToArray()));
        }

        internal void Publish(List<Change> changes) => (_changes ??= []).AddRange(changes);
        public IReadOnlyList<Change> Drain()
        {
            lock (_gate)
            {
                var result = _changes;
                _changes = null;
                return result is null ? Array.Empty<Change>() : result;
            }
        }
    }
}
