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
        private readonly HashSet<Durable.InboxKey> _repliedAfterEmit = [];
        private readonly Durable.InstanceRow _row;
        private Durable.FoundationHeaderChange? _header;
        private Contract.ActorStatus _status;
        private object? _output;
        private bool _emitted;

        public ActorTurn(Durable.InMemoryStore store, Durable.TurnInput input, Contract.ActorHeader header, object? ambient)
        {
            _store = store;
            _input = input;
            _turn = new(input);
            Header = header;
            Ambient = ambient;
            _row = input.Row ?? NewRow(input.Actor, input.Spec!, input.Taken[input.From].Message);
            State = _row.FoundationNullState ? null : _row.State;
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
        public object? Ambient { get; }

        public void Emit(string channel, object? item)
        {
            _emitted = true;
            _turn.Emit(channel, item);
        }

        public void Reply(Contract.Message message, string channel, object? item)
        {
            var request = Received(message);
            _turn.Reply(request, channel, item);
            if (_emitted) _repliedAfterEmit.Add(request.InboxKey);
        }
        public void Hold(Contract.Message message) => _turn.Hold(Received(message));

        public string Send(Contract.ActorRef target, string kind, string? payload = null, long? dueAt = null)
        {
            HeaderLimits.RequireKind(kind, nameof(kind));
            HeaderLimits.RequireContent(payload, nameof(payload));
            var address = target == Header.Ref ? new Durable.CreationAddress(_row.Id, _row.Creation)
                : _stagedChildren.GetValueOrDefault(target) ?? _store.FoundationTarget(Header.Tenant, target.Id) ??
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
            HeaderLimits.ValidateStart(spec.Type, spec.TypeData, spec.Input);
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
            HeaderLimits.RequireName(key, nameof(key), preservesCodeUnits: true);
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout ?? TimeSpan.Zero, TimeSpan.Zero, nameof(timeout));
            long? deadline = timeout is { } period ? checked(Now + (long)period.TotalMilliseconds) : null;
            try { _turn.StartJobWithPayload(key, arguments, deadline, boundMessage: true); }
            catch (Durable.MetadataLimitExceeded error)
            {
                throw new Contract.MetadataLimitExceeded(error.ParamName, error.LimitBytes);
            }
        }

        public void CancelJob(string key)
        {
            var ticketKey = Durable.InboxKey.Ticket(_row.Id, key, _row.Creation);
            var ticket = _store.FoundationJob(_row.Id, _row.Creation, key).Ticket;
            if (ticket is null && _input.VolatileMessage(ticketKey) is { Kind: Durable.Kinds.Job } local)
                ticket = local.Read<Durable.JobTicket>();

            var pending = ticket is null ? (Durable.InboxKey?)null : ticketKey;
            if (_store.HasFoundationJobResult(_row.Id, _row.Creation, key))
                pending = Durable.InboxKey.JobResult(_row.Id, key, _row.Creation);

            var deadline = ticket?.Timer;
            if (deadline is not null && !_store.HasStoredJobDeadline(_row.Id, _row.Creation, deadline) &&
                _input.VolatileMessage(Durable.InboxKey.Issued(deadline)) is not { Kind: Durable.Kinds.JobDeadline })
                deadline = null;

            _turn.CancelJob(key, pending, deadline);
        }

        public void Subscribe(string key, string wait, bool keep = false)
        {
            HeaderLimits.RequireName(key, nameof(key));
            HeaderLimits.RequireName(wait, nameof(wait));
            _turn.Subscribe(Qualify(key), wait, keep, Header.Tenant);
        }

        public void Unsubscribe(string key, string wait) => _turn.Unsubscribe(Qualify(key), wait, Header.Tenant);

        public string Publish(string key, string? payload, TimeSpan keepFor)
        {
            HeaderLimits.RequireName(key, nameof(key));
            HeaderLimits.RequireContent(payload, nameof(payload), Durable.FoundationMetadataLimits.EventContentBytes);
            return _turn.Publish(Qualify(key), payload, keepFor, _store, Header.Tenant);
        }

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
            HeaderLimits.ValidateTypeData(typeData);
            if (labels.Count > HeaderLimits.MaxLabels || labels.Any(pair =>
                Encoding.UTF8.GetByteCount(pair.Key) > HeaderLimits.MaxLabelBytes || Encoding.UTF8.GetByteCount(pair.Value) > HeaderLimits.MaxLabelBytes))
                throw new ArgumentException("An actor may project four labels of at most 1,024 bytes per key and value.", nameof(labels));
            _header = new(typeData, labels.ToFrozenDictionary(StringComparer.Ordinal));
        }

        public Durable.TurnOutcome Seal()
        {
            var wasRunning = _row.Status == Durable.InstanceStatus.Running;
            var ended = wasRunning && _status != Contract.ActorStatus.Running;
            var outcome = _turn.Outcome((Durable.InstanceStatus)_status);
            var cascades = outcome.Consumed.Any(delivery => delivery.Message.Kind == Contract.Kinds.Terminate) ||
                ended && _status == Contract.ActorStatus.Cancelled;
            if (cascades)
            {
                foreach (var child in _children) _turn.Send(new(child.Id), Contract.Kinds.Terminate, options: new(TargetCreation: child.Creation));
                foreach (var job in _turn.Jobs() is { } jobs ? Durable.DurableKeyJson.ReadKeys(jobs) : [])
                    CancelJob(job);
            }

            outcome.Replied.RemoveAll(reply => _repliedAfterEmit.Contains(reply.Message));
            for (var i = 0; i < outcome.Replied.Count; i++)
                outcome.Replied[i] = outcome.Replied[i] with { FromFoundation = true };
            foreach (var notice in outcome.Consumed.Where(entry => entry.Message.Kind == Contract.Kinds.ChildFinished))
            {
                var message = notice.Message;
                if (message.Read<Completion>().Final) _children.Remove(new(message.Sender!, message.SenderCreation));
            }

            var row = _row with { State = State ?? "null", FoundationNullState = State is null,
                Revision = _turn.Revision, Status = (Durable.InstanceStatus)_status, Jobs = _turn.Jobs(), Children = Durable.ChildRegistry.Write(_children) };
            if (ended)
            {
                if (row.Parent is not null) _turn.AddTerminalNotice(Notice(row, _output is null ? null : Durable.Json.Write(_output), final: true));
                outcome.Unsubscribe = true;
            }
            if (_input.Row is null) _entries.Insert(0, "start");
            if (ended) _entries.Add("end");
            if (Inbox is [{ Kind: Contract.Kinds.Migrate }]) _entries.Add("migrate");
            var entries = _input.Pending is List<object> pending ? [.. pending, .. _entries] : _entries;
            var journal = entries.Count > 0 ? Durable.Json.Write(entries) :
                _row.History == Durable.History.State && State != (_row.FoundationNullState ? null : _row.State) ? "[]" : null;
            outcome.FoundationHeader = _header ?? _row.FoundationInlineHeader ?? (_input.Row is null ? InitialHeader(Header.TypeData) : null);
            if (row.FoundationInlineBirth is not null)
            {
                var until = _input.Unwritten ? _input.Until : InlineUntil();
                if (ended && (_input.Row is null || _input.CheckpointTime < until) && _turn.SentCount == 0 && _turn.SubscribedCount == 0 &&
                    outcome.Held.Count == 0 && outcome.Consumed.TrueForAll(item => !item.Durable))
                    return outcome;

                if (!ended && outcome.Held.Count == 0 && _turn.CanWaitInMemory(until))
                {
                    _turn.WaitInMemory();
                    outcome.InMemory = true;
                    outcome.Until = until;
                    outcome.Pending = entries;
                    return _turn.Seal(outcome, row with { FoundationInlineHeader = outcome.FoundationHeader }, State, null);
                }
            }
            return _turn.Seal(outcome, row, State, journal);
        }

        private long InlineUntil()
        {
            var configured = (long)_input.Services.ShortWait.TotalMilliseconds;
            var deadline = Durable.ReplyTo.Parse(_input.Taken[_input.From].Message.ReplyTo!).Deadline;
            return Math.Min(checked(Now + configured), deadline);
        }

        private Durable.Message Received(Contract.Message message) =>
            _messages.TryGetValue(message, out var received) ? received :
                throw new ArgumentException("The message was not received in this turn.", nameof(message));

        private string Qualify(string key) => Header.Tenant + "_" + key;

        private Contract.Message Project(Durable.Message message)
        {
            var sender = message.Sender == _row.Id && message.SenderCreation == _row.Creation ? Header.Ref
                : _store.FoundationReferenceOf(Header.Tenant, message.Sender, message.SenderCreation) is { } reference
                    ? new Contract.ActorRef(reference) : (Contract.ActorRef?)null;
            var logicalId = message.Kind == Contract.Kinds.JobFinished && !message.FoundationJobResult ? message.Id + "/result" : message.Id;
            var id = (message.InboxKey.HasIssuedOrigin || message.InboxScope == Durable.InboxKey.EventCreatorScope) && message.StorageId is null
                ? _store.MessageHandle("inbox:" + logicalId) : message.Id;
            if (message.Start is null && Durable.Events.MatchFor(message) is { } match)
            {
                if (!match.Key.StartsWith(Qualify(""), StringComparison.Ordinal))
                    throw new InvalidOperationException("An event from another scope reached this turn.");
                var key = match.Key[(Header.Tenant.Length + 1)..];
                var delivery = new Contract.EventDelivered(key, match.Event, match.Wait);
                return new(match.Event, Contract.Kinds.Event, message.Payload, Event: delivery);
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
