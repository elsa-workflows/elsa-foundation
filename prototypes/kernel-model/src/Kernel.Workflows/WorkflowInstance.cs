using Kernel.Durable;
using Kernel.Runtime;

namespace Kernel.Workflows;

/// <summary>
/// The workflow instance, layer 3's actor type: what every workflow has whatever its model. Definition and version,
/// status, parent and children, cancel of a tree, suspend, resume, terminate, rebind and delete, the history envelope;
/// and an execution model (<see cref="IExecutionModel"/>) for the rest, found by the row's model key.
/// </summary>
public sealed class WorkflowInstance(IEnumerable<IExecutionModel> models) : IActorType
{
    private readonly Dictionary<string, IExecutionModel> _models = models.ToDictionary(m => m.Key, StringComparer.Ordinal);
    public bool SingleMessage(Message message) => message.Kind == WorkflowKinds.ChildFinished;
    public bool SingleMessage(string? model, InstanceRow? row, Delivery delivery) =>
        row is { Status: not InstanceStatus.Running } && delivery.IsDurableJobTicket && delivery.Message.Kind == Kinds.Job ||
        row is { Creation: > 0 } && SingleMessage(delivery.Message);

    public bool RevisitsHeld(string? model, InstanceRow row, Delivery delivery) =>
        SingleMessage(model, row, delivery) || SingleMessage(delivery.Message);

    /// <summary>
    /// A running instance is suspended, or its row was written by a newer version of its model than this node has (finding F14: an older model
    /// would read the newer state wrongly and write it back older).
    /// </summary>
    public bool Withholds(InstanceRow row, ITurnServices services) =>
        row.Status == InstanceStatus.Running && ((row.Suspended && !services.Has(Flaw.DeliverWhileSuspended)) || (row.StateVersion > _models[row.Model].StateVersion && !services.Has(Flaw.TurnNewerState)));

    public TurnOutcome? Command(TurnInput input)
    {
        var (row, first) = (input.Row, input.Taken[0]);
        if (row is { Creation: > 0 } && first.Message.Kind == WorkflowKinds.ChildFinished)
        {
            try
            {
                if (first.Message.Payload is null || first.Message.Read<ChildFinished>() is not { } finished)
                    return CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidChildCompletion");
                if (!ChildRegistry.Read(row.Children).Contains(new(finished.Child, finished.Creation)))
                    return CreationProtocol.Refuse(input.Actor, row, input.Taken, "UnknownChildCreation");
            }
            catch (System.Text.Json.JsonException) { return CreationProtocol.Refuse(input.Actor, row, input.Taken, "InvalidChildCompletion"); }
        }
        if (row is { } current && Kinds.IsControl(first.Message.Kind) && (current.Status == InstanceStatus.Running || current.Children is not null || first.Message.Kind == Kinds.Terminate && current.Jobs is not null || first.Message.Kind == Kinds.Delete))
            return Control(input, first);       // a terminate still goes on to the children of a finished instance
        if (row is { Status: not InstanceStatus.Running } && _models[row.Model] is IModelMetadata reader &&
            row.StateVersion > _models[row.Model].StateVersion && !input.Services.Has(Flaw.TurnNewerState) &&
            input.Taken.Any(d => reader.HandlesMetadata(d.Message)))
        {
            // Keep private future state opaque, answer the capability mismatch, and still drain ordinary late input.
            var next = row with { Revision = row.Revision + 1, Jobs = RemainingJobs(row, input.Taken) };
            var unavailable = new ModelMetadataUnavailable(input.Actor, row.Model, row.StateVersion, _models[row.Model].StateVersion);
            var result = new TurnOutcome(input.Actor) { Row = next, Expected = row.Revision, Revision = next.Revision,
                Status = row.Status, Consumed = [.. input.Taken], Journal = Journal(row.History, [unavailable]) };
            foreach (var item in input.Taken.Where(d => reader.HandlesMetadata(d.Message)))
                result.ReplyTo(item, unavailable);
            return result;
        }
        if (row is { Status: not InstanceStatus.Running } &&
            !(_models[row.Model] is IModelMetadata metadata && input.Taken.Any(d => metadata.HandlesMetadata(d.Message))))
        {
            var unit = new TurnOutcome(input.Actor) { Consumed = [.. input.Taken], Status = row.Status };
            var jobs = RemainingJobs(row, input.Taken);
            if (jobs != row.Jobs)
            {
                unit.Row = row with { Jobs = jobs, Revision = row.Revision + 1 };
                unit.Expected = row.Revision;
                unit.Revision = unit.Row.Revision;
                unit.Journal = Journal(row.History, ["job ticket consumed"]);
            }
            return unit;
        }
        return null;
    }

    public async ValueTask<TurnOutcome> TurnAsync(TurnInput input)
    {
        var turn = new Turn(input);
        var model = _models[turn.Instance.Model];
        turn.State = input.State ?? (input.Row is { } row ? ReadState(row, model, input.Services) : model.CreateState(input.Spec!));
        input.Started(turn.Inbox);
        if (input.Row is { Status: not InstanceStatus.Running } && model is IModelMetadata metadata)
            await metadata.MetadataAsync(turn, input.Row!.Status, CancellationToken.None);
        else await model.TurnAsync(turn, CancellationToken.None);
        return turn.Seal(model.StateVersion);
    }

    public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) => _models[model].ExecuteJobAsync(job, cancellationToken);

    /// <summary>Park: the instance becomes faulted with the error, the messages are consumed, the partition goes on.</summary>
    public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
        Park(row, actor, taken, spec, error, null);

    public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error, object? pending)
    {
        var ended = row is { Status: not InstanceStatus.Running };
        var next = row is { } r ? r with { Status = ended ? r.Status : InstanceStatus.Faulted, Revision = r.Revision + 1,
                Fault = ended ? r.Fault : error, Jobs = RemainingJobs(r, taken) }
            : spec is null ? null
            : new InstanceRow(actor, spec.Model, spec.Definition, InstanceStatus.Faulted, "null", 1, spec.Parent, error, History: spec.History,
                Creation: spec.Creation?.Generation ?? 0, ParentCreation: spec.ParentCreation); // owner binds its staged start envelope, even when the model held all input
        if (next is not null && row is not { Status: not InstanceStatus.Running }) next = WorkflowCommitClock.End(next);
        var unit = new TurnOutcome(actor) { Row = next, Expected = row?.Revision, Spec = spec, Consumed = [.. taken], Journal = next is null ? null : Journal(next.History, [.. pending as List<object> ?? [], (ended ? "late input failed: " : "parked: ") + error]), Status = next?.Status, Unsubscribe = next is not null };
        if (next?.Parent is not null && !ended) unit.Sent.Add(Finished(next, null));
        return unit;
    }

    // Match the typed ticket of this creation, never an ordinary equal-looking wire ID.
    private static string? RemainingJobs(InstanceRow row, IReadOnlyList<Delivery> taken)
    {
        if (row.Jobs is null) return null;
        var consumed = taken.Where(i => (i.IsDurableJobTicket || i.IsWorkerJobCompletion) && i.Message.Target.Id == row.Id)
            .Select(i => i.Message.InboxKey).ToHashSet();
        if (consumed.Count == 0) return row.Jobs;
        var jobs = DurableKeyJson.ReadKeys(row.Jobs);
        if (jobs.RemoveAll(key => consumed.Contains(InboxKey.Ticket(row.Id, key, row.Creation))) == 0) return row.Jobs;
        return jobs.Count == 0 ? null : DurableKeyJson.WriteKeys(jobs);
    }

    /// <summary>Decision 4: an instance kept in memory past its short wait is written as it is, at the revision it has.</summary>
    public TurnOutcome Write(InstanceRow row, object? state, object? pending) =>
        new(row.Id) { Row = row, State = state, Status = row.Status, Revision = row.Revision, Journal = Journal(row.History, [.. pending as List<object> ?? [], "written"]) };

    /// <summary>The instance's own turn: suspend, resume, terminate or delete. A message that changes nothing is consumed and dropped.</summary>
    private object ReadState(InstanceRow row, IExecutionModel model, ITurnServices services) =>
        row.StateVersion < model.StateVersion && !services.Has(Flaw.SkipUpgrade)
            ? model.UpgradeState(row.State, row.StateVersion) : Json.Read(row.State, model.StateType);

    private TurnOutcome Control(TurnInput input, Delivery item)
    {
        var (row, instance) = (input.Row!, input.Actor);
        if (item.Message.Kind == Kinds.Terminate) return Terminate(input, item, row);
        if (item.Message.Kind == Kinds.Delete)
            // Only an ended instance is deleted. What arrives for it afterwards is dropped as for a missing target.
            return row.Status == InstanceStatus.Running || row.StateVersion > _models[row.Model].StateVersion ||
                (_models[row.Model] is IModelMetadata metadata && metadata.ProtectsDeletion(ReadState(row, _models[row.Model], input.Services), input.Services.Now, new DurableTurn(input).Clock)) ? new TurnOutcome(instance) { Consumed = [item], State = input.State, Status = row.Status }
                : new TurnOutcome(instance) { Delete = true, Row = row.Creation > 0 ? CreationProtocol.Retire(row) : null,
                    Expected = row.Revision, Consumed = [item], Unsubscribe = true, Status = row.Status, Journal = Json.Write(new[] { "deleted" }) };
        var suspended = item.Message.Kind == Kinds.Suspend;
        if (row.Status != InstanceStatus.Running) return new TurnOutcome(instance) { Consumed = [item], Status = row.Status };
        if (row.Suspended == suspended) return new TurnOutcome(instance) { Consumed = [item], State = input.State };
        var next = row with { Suspended = suspended, Revision = row.Revision + 1 };
        return new TurnOutcome(instance) { Row = next, Expected = row.Revision, Consumed = [item], State = input.State, Revision = next.Revision, Journal = Journal(row.History, [item.Message.Kind]) };
    }

    /// <summary>
    /// Forced termination: the instance ends Cancelled (if it still ran), its background jobs are cancelled, its waits are
    /// deleted, its parent is told, and a terminate goes to every child it started that has not finished. A child whose
    /// start is still on its way gets the terminate after the start: one sender, one target, in the order sent.
    /// </summary>
    private TurnOutcome Terminate(TurnInput input, Delivery item, InstanceRow row)
    {
        var instance = input.Actor;
        var running = row.Status == InstanceStatus.Running;
        var next = row with { Status = running ? InstanceStatus.Cancelled : row.Status, Revision = row.Revision + 1, Jobs = null, Children = null, Suspended = false };
        if (running && row.StateVersion <= _models[row.Model].StateVersion && _models[row.Model] is IModelMetadata metadata)
        {
            var state = ReadState(row, _models[row.Model], input.Services);
            metadata.TerminateMetadata(state);
            next = next with { State = Json.Write(state), StateVersion = _models[row.Model].StateVersion };
        }
        if (running) next = WorkflowCommitClock.End(next);
        var children = !input.Services.Has(Flaw.TerminateRootOnly) ? ChildRegistry.Read(row.Children) : [];
        var unit = new TurnOutcome(instance)
        {
            Row = next, Expected = row.Revision, Consumed = [item], Status = next.Status, Revision = next.Revision, Unsubscribe = running,
            Journal = Journal(row.History, ["terminated"]),
            Sent = children.Select((child, i) => new Message($"{Origin(row)}#{next.Revision}.{i:D4}", new InstanceRef(child.Id), Kinds.Terminate, Sender: instance,
                TargetCreation: child.Creation > 0 ? child.Creation : null, SenderCreation: row.Creation).AsIssued()).ToList(),
            Cancelled = (row.Jobs is { } jobs ? DurableKeyJson.ReadKeys(jobs) : []).Select(key => InboxKey.Ticket(instance, key, row.Creation)).ToList(),
        };
        foreach (var ticket in unit.Cancelled) input.Services.Cancelling(ticket);
        if (running && row.Parent is not null) unit.Sent.Add(Finished(next, null));
        return unit;
    }

    /// <summary>The history envelope: what a commit leaves in the journal at a history level. At Minimal no row is written and the position does not move.</summary>
    internal static string? Journal(History history, IEnumerable<object> entries) =>
        history switch { History.Full => Json.Write(entries), History.State => "[]", _ => null };

    /// <summary>The parent's notice that a child ended.</summary>
    internal static Message Finished(InstanceRow row, string? output) =>
        new Message($"{Origin(row)}#{row.Revision}.end", new InstanceRef(row.Parent!), WorkflowKinds.ChildFinished, Json.Write(new ChildFinished(row.Id, row.Status, output, row.Creation)), row.Id,
            TargetCreation: row.ParentCreation > 0 ? row.ParentCreation : null, SenderCreation: row.Creation).AsIssued();

    private static string Origin(InstanceRow row) => CreationProtocol.Origin(row.Id, row.Creation);
}
