using System.Collections.Frozen;
using System.Text;
using Kernel.Runtime;
using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed partial class ActorAdapter
{
    private sealed class ActorTurn : Contract.IActorTurn
    {
        private readonly Durable.InMemoryStore _store;
        private readonly Durable.TurnInput _input;
        private readonly Durable.DurableTurn _turn;
        private readonly List<Durable.CreationAddress> _children;
        private readonly Dictionary<Contract.ActorRef, Durable.CreationAddress> _stagedChildren = [];
        private readonly Dictionary<Contract.Message, Durable.Message> _messages = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _entries = [];
        private readonly Durable.InstanceRow _row;
        private Durable.FoundationHeaderChange? _header;
        private Contract.ActorStatus _status;
        private object? _output;

        public ActorTurn(Durable.InMemoryStore store, Durable.TurnInput input, Contract.ActorHeader header)
        {
            _store = store;
            _input = input;
            _turn = new(input);
            Header = header;
            _row = input.Row ?? NewRow(input.Actor, input.Spec!, input.Taken[input.From].Message);
            State = input.Row is null ? null : input.Row.State;
            _status = header.Status;
            _children = Durable.ChildRegistry.Read(_row.Children);
            var inbox = new List<Contract.Message>();
            foreach (var entry in _turn.Inbox)
            {
                var message = Project(entry);
                _messages.Add(message, entry);
                inbox.Add(message);
            }
            Inbox = inbox.AsReadOnly();
        }

        public Contract.ActorHeader Header { get; }
        public long Now => _input.Services.Now;
        public IReadOnlyList<Contract.Message> Inbox { get; }
        public string? State { get; set; }
        public object? Ambient => _input.Ambient;

        public void Emit(string channel, object? item) => _turn.Emit(channel, item);
        public void Reply(Contract.Message message, string channel, object? item) => _turn.Reply(Received(message), channel, item);
        public void Hold(Contract.Message message) => _turn.Hold(Received(message));

        public string Send(Contract.ActorRef target, string kind, string? payload = null, long? dueAt = null)
        {
            var address = _stagedChildren.GetValueOrDefault(target) ?? _store.FoundationTarget(Header.Tenant, target.Id) ??
                throw new ArgumentException("The actor reference was not found.", nameof(target));
            if (kind is Contract.Kinds.Start or Contract.Kinds.Event or Contract.Kinds.ChildFinished or Contract.Kinds.JobFinished)
                throw new ArgumentException("The foundation issues this message kind.", nameof(kind));
            return _turn.SendFromActor(_store, new(address.Id), address.Creation, kind, payload, dueAt);
        }

        public void Recall(string id)
        {
            if (id.Length != 65 || id[0] != '~' || id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") >= 0)
                throw new ArgumentException("Recall requires an issued message id.", nameof(id));
            _turn.RecallFromActor(_store, id);
        }

        public Contract.ActorRef Start(Contract.StartSpec spec, Contract.Placement placement = Contract.Placement.WithParent, bool detached = false)
        {
            var destination = placement == Contract.Placement.Spread
                ? InstanceRef.Spread(FormattableString.Invariant($"{Header.Ref.Id}/{_turn.Revision}/{_turn.Ordinal}"), _input.Services.Partitions)
                : _turn.Actor.Partition;
            var creator = new Durable.StartSpec(spec.Type, spec.TypeData, spec.Input, detached ? null : _row.Id, spec.Ephemeral,
                (Durable.History)spec.History, ParentCreation: detached ? 0 : _row.Creation);
            var child = new Durable.CreationAddress(_turn.StartFoundationChild(creator, destination).Id, 1);
            var reference = new Contract.ActorRef(_store.ReferenceFor(Header.Tenant, child));
            _stagedChildren.Add(reference, child);
            if (!detached) _children.Add(child);
            return reference;
        }

        public void StartJob(string key, string? arguments = null, TimeSpan? timeout = null)
        {
            if (timeout is not null) throw new NotSupportedException("Public job deadlines are unavailable on this adapter.");
            _turn.StartJobWithPayload(key, arguments);
        }

        public void CancelJob(string key) => _turn.CancelJob(key);

        public void Subscribe(string key, string wait, bool keep = false) => _turn.Subscribe(Qualify(key), wait, keep, Header.Tenant);
        public void Unsubscribe(string key, string wait) => _turn.Unsubscribe(Qualify(key), wait);
        public string Publish(string key, string? payload, TimeSpan keepFor) => _turn.Publish(Qualify(key), payload, keepFor, _store);

        public void Record(object entry)
        {
            if (_row.History == Durable.History.Full) _entries.Add(System.Text.Json.JsonSerializer.SerializeToElement(entry));
        }

        public void End(Contract.ActorStatus status, object? output = null)
        {
            if (_status != Contract.ActorStatus.Running || status is not (Contract.ActorStatus.Finished or Contract.ActorStatus.Faulted or Contract.ActorStatus.Cancelled))
                throw new InvalidOperationException("Only a running actor can end, once, with a final status.");
            (_status, _output) = (status, output);
        }

        public void SetHeader(string typeData, IReadOnlyDictionary<string, string> labels)
        {
            if (labels.Count > 4 || labels.Any(pair => Encoding.UTF8.GetByteCount(pair.Key) > 1024 || Encoding.UTF8.GetByteCount(pair.Value) > 1024))
                throw new ArgumentException("An actor may project four labels of at most 1,024 bytes per key and value.", nameof(labels));
            _header = new(typeData, labels.ToFrozenDictionary(StringComparer.Ordinal));
        }

        public Durable.TurnOutcome Seal()
        {
            var wasRunning = _row.Status == Durable.InstanceStatus.Running;
            var cascades = Inbox is [{ Kind: Contract.Kinds.Terminate }] || wasRunning && _status == Contract.ActorStatus.Cancelled;
            var ended = wasRunning && _status != Contract.ActorStatus.Running;
            if (ended || cascades) _turn.HoldNothing();
            if (cascades)
            {
                foreach (var child in _children) _turn.Send(new(child.Id), Contract.Kinds.Terminate, options: new(TargetCreation: child.Creation));
                foreach (var job in _turn.Jobs() is { } jobs ? Durable.DurableKeyJson.ReadKeys(jobs) : [])
                    _turn.CancelJob(job);
            }

            var outcome = _turn.Outcome((Durable.InstanceStatus)_status);
            foreach (var notice in outcome.Consumed.Where(entry => entry.Message.Kind == Contract.Kinds.ChildFinished))
            {
                var message = notice.Message;
                if (message.Read<Completion>().Final) _children.Remove(new(message.Sender!, message.SenderCreation));
            }

            var row = _row with { State = State ?? "null", Revision = _turn.Revision, Status = (Durable.InstanceStatus)_status,
                Jobs = _turn.Jobs(), Children = Durable.ChildRegistry.Write(_children) };
            if (ended)
            {
                if (row.Parent is not null) _turn.AddTerminalNotice(Notice(row, _output is null ? null : Durable.Json.Write(_output), final: true));
                outcome.Unsubscribe = true;
            }
            if (_input.Row is null) _entries.Insert(0, "start");
            if (ended) _entries.Add("end");
            if (Inbox is [{ Kind: Contract.Kinds.Migrate }]) _entries.Add("migrate");
            var journal = _entries.Count > 0 ? Durable.Json.Write(_entries) :
                _row.History == Durable.History.State && State != _input.Row?.State ? "[]" : null;
            outcome.FoundationHeader = _header;
            return _turn.Seal(outcome, row, State, journal);
        }

        private Durable.Message Received(Contract.Message message) =>
            _messages.TryGetValue(message, out var received) ? received :
                throw new ArgumentException("The message was not received in this turn.", nameof(message));

        private string Qualify(string key) => Header.Tenant + "_" + key;

        private Contract.Message Project(Durable.Message message)
        {
            var sender = _store.FoundationReferenceOf(Header.Tenant, message.Sender, message.SenderCreation) is { } reference
                ? new Contract.ActorRef(reference) : (Contract.ActorRef?)null;
            var id = message.InboxKey.HasIssuedOrigin && message.StorageId is null
                ? _store.MessageHandle("inbox:" + message.Id) : message.Id;
            if (message.EventMatch is { } encoded)
            {
                var match = Durable.EventMatch.Decode(encoded);
                if (!match.Key.StartsWith(Qualify(""), StringComparison.Ordinal))
                    throw new InvalidOperationException("An event from another scope reached this turn.");
                var key = match.Key[(Header.Tenant.Length + 1)..];
                var delivery = new Contract.EventDelivered(key, match.Event, match.Wait);
                return new(match.Event, message.Start is null ? Contract.Kinds.Event : Contract.Kinds.Start, message.Payload, Event: delivery);
            }
            if (message.Kind == Contract.Kinds.ChildFinished)
            {
                var result = message.Read<Completion>();
                var payload = Durable.Json.Write(new Contract.ChildFinished(sender!.Value, (Contract.ActorStatus)result.Status, result.Output, result.Final));
                return new(id, message.Kind, payload, sender);
            }
            return new(id, message.Start is null ? message.Kind : Contract.Kinds.Start, message.Start?.Input ?? message.Payload, sender);
        }
    }
}
