using System.Text;
using Kernel.Runtime;
using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Runs a contract actor in the owner's turn. The adapter only stages effects.</summary>
internal sealed partial class ActorAdapter(Durable.InMemoryStore store, Contract.IActorType actor,
    (Durable.Requests Requests, string Node)? local = null) : Durable.IActorType
{
    private const int MaxTurnSends = 10_000;
    private const int TerminalEnvelopeLimit = 16_457_728;
    private const int TerminalHeaderReserve = 16_384;
    private const int TerminalChildBytes = 384;
    private const int TerminalJobBytes = 12_416;

    public bool SupportsFoundationOperator(Durable.InstanceRow? row) => row?.Model == actor.Type;

    public bool Withholds(Durable.InstanceRow row, Durable.ITurnServices services)
    {
        var checkpoint = store.FoundationTurnHeader(row);
        return checkpoint.Park is not null || actor.Withholds(Header(checkpoint));
    }

    bool Durable.IActorType.SettlesWhileWithheld(Durable.InstanceRow row, Durable.Delivery delivery) =>
        (delivery.Message.Kind == Durable.Kinds.JobDeadline || delivery.IsWorkerJobCompletion && delivery.JobDeadline is not null) &&
        store.FoundationTurnHeader(row).Park is null;

    public bool SingleMessage(Durable.Message message) => message.Kind is Contract.Kinds.ChildFinished or Durable.Kinds.JobDeadline;

    public bool SelectsAlone(string? model, Durable.InstanceRow? row, Durable.Delivery delivery) =>
        !delivery.Held && (SingleMessage(delivery.Message) || delivery.Message.ReplyTo is not null ||
            delivery.IsWorkerJobCompletion && delivery.JobDeadline is not null);

    public Durable.TurnOutcome? Command(Durable.TurnInput input)
    {
        var delivery = input.Taken[input.From];
        var message = delivery.Message;
        if (message.Kind == Durable.Kinds.JobDeadline)
            return TimeoutJob(input, message);
        if (delivery.IsWorkerJobCompletion && delivery.JobDeadline is not null && input.Row is { } completedRow &&
            delivery.Original?.Read<Durable.JobTicket>() is { } ticket)
            return SettleJob(input, completedRow, message.Read<Durable.JobFinished>(), ticket.Timer, cancel: false);
        if (message.Operator is { Selection: { } selection } && input.Row is { } selected)
        {
            var identity = store.FoundationIdentity(selected.Id, selected.Creation);
            if (!store.MatchesFoundationSelection(identity.Tenant, identity.Reference, selection))
                return Durable.CreationProtocol.Refuse(input.Actor, selected, input.Taken, "StaleSelection");
        }
        if (message is { Kind: Contract.Kinds.Delete, Operator: not null } && input.Row is { } ended)
        {
            var checkpoint = store.FoundationTurnHeader(ended);
            if (ended.Status == Durable.InstanceStatus.Running || checkpoint.Park is not null ||
                ended.Children is not null || ended.Jobs is not null)
                return Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, "Protected");
            try
            {
                if (actor.ProtectsDeletion(Header(checkpoint), ended.State, input.Services.Now))
                    return Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, "Protected");
            }
            catch (Exception)
            {
                return Durable.CreationProtocol.Refuse(input.Actor, ended, input.Taken, "ProtectionUnavailable");
            }

            var marker = Durable.CreationProtocol.Retire(ended);
            return new(input.Actor)
            {
                Row = marker, Expected = ended.Revision, Revision = marker.Revision, Status = ended.Status,
                Consumed = [.. input.Taken], Journal = Durable.Json.Write(new[] { "delete" }), Delete = true, Unsubscribe = true
            };
        }
        if (message.Kind is Contract.Kinds.Recover or Contract.Kinds.Discard or Contract.Kinds.Delete ||
            message.Kind == Contract.Kinds.Terminate && input.Row is { } row && store.FoundationTurnHeader(row).Park is not null)
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "ResolutionUnavailable");
        if (message is { ActorAuthored: true, Kind: Contract.Kinds.Terminate } &&
            input.Row is { Status: not Durable.InstanceStatus.Running } terminal)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = terminal.Status };
        if (message.Kind != Contract.Kinds.ChildFinished) return null;

        if (!IsChildNotice(input.Row, message))
            return Durable.CreationProtocol.Refuse(input.Actor, input.Row, input.Taken, "InvalidChildCompletion");
        return null;
    }

    private Durable.TurnOutcome TimeoutJob(Durable.TurnInput input, Durable.Message message)
    {
        var row = input.Row;
        if (!message.InboxKey.HasIssuedOrigin || row is null ||
            message.Sender != row.Id || message.SenderCreation != row.Creation)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = row?.Status };
        var key = message.Read<Durable.JobDeadline>().Key;
        var (ticket, attempts) = store.FoundationJob(row.Id, row.Creation, key);
        if (ticket is null || ticket.Timer != message.Id || ticket.Deadline != message.Due || attempts >= input.MaxAttempts)
            return new(input.Actor) { Consumed = [.. input.Taken], Status = row.Status };

        return SettleJob(input, row, new Durable.JobFinished(key, null, "timeout"), null, cancel: true);
    }

    private Durable.TurnOutcome SettleJob(Durable.TurnInput input, Durable.InstanceRow row, Durable.JobFinished result,
        string? timer, bool cancel)
    {
        var turn = new Durable.DurableTurn(input);
        if (cancel) turn.CancelJob(result.Key);
        else if (timer is not null && store.HasStoredJobDeadline(row.Id, row.Creation, timer)) turn.Recall(timer);
        turn.ReportFoundationJobResult(result);
        // The logical job remains pending until the actor applies or cancels this recorded result.
        var checkpoint = row with { Revision = turn.Revision };
        return turn.Seal(turn.Outcome(row.Status), checkpoint, input.State, journal: null);
    }

    private static bool IsChildNotice(Durable.InstanceRow? parent, Durable.Message message) =>
        parent is not null && message.Target.Id == parent.Id && message.TargetCreation == parent.Creation &&
        message.Sender is { } child && Durable.CreationProtocol.HasTerminalNoticeId(message, child, message.SenderCreation) &&
        Durable.ChildRegistry.Read(parent.Children).Contains(new(child, message.SenderCreation));

    public async ValueTask<Durable.TurnOutcome> TurnAsync(Durable.TurnInput input)
    {
        var header = input.Row is { } row ? Header(store.FoundationTurnHeader(row)) : StartHeader(input);
        var delivery = input.Taken[input.From];
        var ambient = local is { } host && input.Taken.Count - input.From == 1 && delivery.Message.ReplyTo is { } address
            ? host.Requests.TakeLocalAmbient(address, host.Node, header.Tenant, input.Services.Now, AmbientDelivery(delivery.Message))
            : null;
        var turn = new ActorTurn(store, input, header, ambient);
        input.Started(input.Taken.Skip(input.From).Select(delivery => delivery.Message).ToArray());
        await actor.TurnAsync(turn, CancellationToken.None);
        var outcome = turn.Seal();
        if (outcome.Sent.Count > MaxTurnSends)
            throw new InvalidOperationException($"A turn may send at most {MaxTurnSends:N0} messages.");
        if (outcome.Row is { } nextRow)
        {
            var nextHeader = header with
            {
                Status = (Contract.ActorStatus)nextRow.Status,
                TypeData = outcome.FoundationHeader?.TypeData ?? header.TypeData,
                Labels = outcome.FoundationHeader?.Labels ?? header.Labels
            };
            ValidateTerminalEnvelope(nextRow, nextHeader);
            if (nextRow.Status == Durable.InstanceStatus.Running && outcome.FoundationHeader is not null)
                outcome.FoundationWithholds = actor.Withholds(nextHeader);
        }
        return outcome;
    }

    private void ValidateTerminalEnvelope(Durable.InstanceRow row, Contract.ActorHeader header)
    {
        var growth = row.Status == Durable.InstanceStatus.Running ? actor.TerminalGrowth(header, row.State) : 0;
        // This is the built-in in-memory encoding. Keep enough space for the terminal registry even when the live row is smaller.
        var children = row.Children is null ? 0 : Durable.ChildRegistry.Read(row.Children).Count;
        var jobs = row.Jobs is null ? 0 : Durable.DurableKeyJson.ReadKeys(row.Jobs).Count;
        var bytes = TerminalHeaderReserve + children * (long)TerminalChildBytes + jobs * (long)TerminalJobBytes
            + Bytes(row.State) + Bytes(row.CustomHandles) + Bytes(row.CommitTimes) + Bytes(row.Model) + Bytes(row.Definition)
            + Bytes(header.TypeData) + header.Labels.Sum(label => Bytes(label.Key) + Bytes(label.Value));
        if (growth < 0 || growth > TerminalEnvelopeLimit - bytes)
            throw new InvalidOperationException("The actor checkpoint and its terminal growth exceed the record envelope.");

        static long Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);
    }

    private static Durable.InboxKey AmbientDelivery(Durable.Message message) => message.EventMatch is { } match
        ? new(message.Target.Id, Durable.InboxKey.EventDeliveryScope, match)
        : message.InboxKey;

    public ValueTask<object?> ExecuteJobAsync(string type, Durable.IJob job, CancellationToken cancellationToken)
    {
        var identity = store.FoundationIdentity(job.Instance.Id, job.Creation);
        return actor.ExecuteJobAsync(new Job(new(identity.Reference), job), cancellationToken);
    }

    public Durable.TurnOutcome Park(Durable.InstanceRow? row, string address, IReadOnlyList<Durable.Delivery> taken,
        Durable.StartSpec? spec, string error)
    {
        var result = new Durable.TurnOutcome(address) { Expected = row?.Revision, Spec = spec, Consumed = [.. taken] };
        if (row is null && spec is null)
        {
            result.Status = null;
            return result;
        }
        var next = row ?? NewRow(address, spec!, taken.First(item => item.Message.Start is not null).Message);
        var running = next.Status == Durable.InstanceStatus.Running;
        next = next with
        {
            Status = running ? Durable.InstanceStatus.Faulted : next.Status,
            Fault = running ? error : next.Fault,
            Revision = checked(next.Revision + 1)
        };
        result.Row = next;
        result.Status = next.Status;
        result.Revision = next.Revision;
        result.Unsubscribe = true;
        result.Journal = Durable.Json.Write(new[] { "parked" });
        if (row is null) result.FoundationHeader = InitialHeader(next.Definition);
        if (running && next.Parent is not null) result.Sent.Add(Notice(next, null, final: false));
        return result;
    }

    private Contract.ActorHeader Header(Durable.InMemoryStore.ActorCheckpoint checkpoint)
    {
        var row = checkpoint.Row;
        var identity = store.FoundationIdentity(row.Id, row.Creation);
        return new(new(identity.Reference), row.Model, identity.Tenant, (Contract.ActorStatus)row.Status, row.Fault,
            Ref(identity.Tenant, row.Parent, row.ParentCreation), checkpoint.EndedAt, checkpoint.Header.TypeData, checkpoint.Header.Labels);
    }

    private Contract.ActorHeader StartHeader(Durable.TurnInput input)
    {
        var spec = input.Spec!;
        var identity = store.FoundationIdentity(input.Actor, input.Creation);
        return new(new(identity.Reference), spec.Model, identity.Tenant, Contract.ActorStatus.Running, null,
            Ref(identity.Tenant, spec.Parent, spec.ParentCreation), null, spec.Definition, InitialHeader(spec.Definition).Labels);
    }

    private Contract.ActorRef? Ref(string tenant, string? id, long creation) =>
        store.FoundationReferenceOf(tenant, id, creation) is { } reference ? new(reference) : null;

    private static Durable.InstanceRow NewRow(string id, Durable.StartSpec spec, Durable.Message creator) =>
        new(id, spec.Model, spec.Definition, Durable.InstanceStatus.Running, "null", 0, spec.Parent, History: spec.History,
            Creation: spec.Creation?.Generation ?? 0, ParentCreation: spec.ParentCreation,
            StartIntent: Durable.CreationProtocol.Fingerprint(spec, creator.Kind, creator.Payload)) { FoundationNullState = true };

    private static Durable.FoundationHeaderChange InitialHeader(string typeData) =>
        new(typeData, System.Collections.Frozen.FrozenDictionary<string, string>.Empty);

    private sealed record Completion(string Child, Durable.InstanceStatus Status, string? Output, long Creation, bool Final);

    private static Durable.Message Notice(Durable.InstanceRow row, string? output, bool final) =>
        new Durable.Message(Durable.CreationProtocol.TerminalNoticeId(row.Id, row.Creation, row.Revision),
            new(row.Parent!), Contract.Kinds.ChildFinished, Durable.Json.Write(new Completion(row.Id, row.Status, output, row.Creation, final)),
            row.Id, TargetCreation: row.ParentCreation, SenderCreation: row.Creation).AsIssued();

    private sealed class Job(Contract.ActorRef actor, Durable.IJob job) : Contract.IJob
    {
        public Contract.ActorRef Actor => actor;
        public string Key => job.Key;
        public string? Arguments => job.Arguments;
        public string AttemptId => job.AttemptId ?? throw new InvalidOperationException("A job execution needs an attempt.");
        public int AttemptNumber => job.AttemptNumber;
        public long? Deadline => job.Deadline;
    }
}
