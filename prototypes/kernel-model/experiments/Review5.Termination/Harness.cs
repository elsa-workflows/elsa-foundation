namespace Kernel.Review5;

/// <summary>What happens around one cascade page's commit, for the takeover and crash schedules.</summary>
enum PageHook { None, TakeoverAfter, CrashLanded, CrashLost }

/// <summary>
/// The proposed termination obligation (R5-B2): the layer-2-neutral list a terminate hands layer 2, children as
/// (<c>InstanceRef</c>, creation) and jobs as (key, <c>JobGeneration</c>), with its state and durable cursor. The
/// prototype's rows have no such columns, so the harness keeps them beside the real row, keyed by actor, and writes
/// them only inside the transaction of a commit that landed (<see cref="Harness.Land"/>).
/// </summary>
sealed class Obligation
{
    public required string Actor;
    public required long Creation;
    public string? Parent;
    public long ParentCreation;
    public List<CreationAddress> Children = [];
    public List<(string Key, long Generation)> Jobs = [];
    public string State = "Held";       // Held (on a marker, awaiting the parent), Discharging, Cleared
    public int Cursor;                  // entries discharged, durable
    public long IdRevision;             // the revision the cascade's child terminate ids carry
    public long MarkerRevision;         // the marker's proposed revision, for a marker (RM-1's revision + 1 per write)
    public int NextControl = 1;         // the ordinal of the next kernel.cascade control
    public int Entries => Jobs.Count + Children.Count;
    public bool Open => State != "Cleared" && Entries > 0;
    public override string ToString() =>
        $"{State}, cursor {Cursor}/{Entries}, parent {Parent ?? "null"} (creation {ParentCreation}), jobs [{string.Join(", ", Jobs.Select(j => $"{j.Key}@g{j.Generation}"))}], children {Children.Count}";
}

/// <summary>One cascade page: what it consumes, sends and clears.</summary>
sealed record Page(int Number, int From, int To, List<InboxKey> Consumed, List<Message> Sends, List<InboxKey> KeptCleared, List<string> Notes, bool More, bool SentControl);

/// <summary>The owner of one partition at one epoch. Its memory dies with it.</summary>
sealed class Owner(string node, int partition, Lease lease, Worker worker)
{
    public readonly string Node = node;
    public readonly int Partition = partition;
    public readonly long Epoch = lease.Epoch;
    public long Position = lease.Position;
    public readonly Worker Worker = worker;
    public readonly HashSet<InboxKey> Settled = [];     // tickets this owner refused and consumed (memory)
    public readonly Dictionary<string, int> MemoryCursor = new(StringComparer.Ordinal);
    public bool Alive = true;
}

sealed record CommitResult(bool Landed, string? Error, Failure? Failure, CommitBatch? Batch);

/// <summary>
/// The kernel the cases run on: the real <see cref="InMemoryStore"/> (4 partitions, a harness clock), one owner per
/// partition taking its fence through the real <c>TakeAsync</c>, and every write a <see cref="CommitBatch"/> through the
/// real <c>CommitAsync</c>, which counts every consumed input and refuses a duplicate send. Turns of the actor type
/// run through the real <c>WorkflowInstance.Command</c> (terminate, delete, ended rows) and the real refusal outcome
/// <c>CreationProtocol.Refuse</c>; they become a <see cref="TurnCommit"/> as Worker.cs 1450–1455 builds one.
/// The delivery order (lowest stored seq first) and the Worker's poll are the harness's.
/// </summary>
sealed class Harness
{
    public const int SendCap = 10_000;                 // the proposed per-turn send cap (owner card: a turn above it fails and parks)
    public const long CascadeScope = 1L << 62;         // the reserved private scope (proposed)
    public const string CascadeKind = "kernel.cascade"; // the proposed private control
    public const string Operator = "operator";
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    public long Now = 1000;
    public readonly InMemoryStore Store;
    public readonly WorkflowInstance Type = new([new StubModel()]);
    public readonly Rules Rules;
    public readonly Report Report;
    public readonly Dictionary<int, Owner> Owners = [];
    private readonly SortedDictionary<long, InboxKey> _queue = [];
    public readonly HashSet<InboxKey> HeldBack = [], Stuck = [], WithModel = [];
    private readonly Dictionary<InboxKey, int> _attempts = [];
    private readonly List<(Owner Owner, string Actor)> _continuations = [];
    private int _nodes;
    public const int RetryLimit = 3;

    // Proposed columns, beside the real rows.
    public readonly Dictionary<string, Obligation> Obligations = new(StringComparer.Ordinal);
    public readonly Dictionary<(string Actor, string Key), long> JobGeneration = [];
    public readonly Dictionary<InboxKey, string> KeptBy = [];
    public readonly HashSet<string> DrainOpen = new(StringComparer.Ordinal);

    // What the run observed.
    public readonly List<Message> Sent = [];
    public readonly Dictionary<string, int> TerminatesDelivered = new(StringComparer.Ordinal);
    public readonly List<string> Refusals = [];
    public readonly List<(string Actor, int Page, IReadOnlyList<InboxKey> Consumed, long Epoch)> CascadeCommits = [];
    public readonly List<bool> StaleReplays = [];
    public readonly List<string> RetirementRefusals = [];
    public int Pages, IdMismatches, LargestCommit;
    public string? ForeignActor;                      // R5-m3's foreign-derivation mutation names this origin
    public Func<string, int, PageHook>? Hook;         // (actor, page) -> takeover or crash around that page's commit
    public Action<string, int>? AfterPage;            // (actor, page) -> a scheduled step after that page landed
    public readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);

    public Harness(Rules rules, Report report)
    {
        Rules = rules;
        Report = report;
        Store = new InMemoryStore(4, () => Now);
        for (var p = 0; p < 4; p++) Become(p, $"n{p}-{++_nodes}");
    }

    public string Name(string id) => Names.GetValueOrDefault(id, id);
    public void Line(string text) => Report.Line(text);
    private bool Shown(Message m) => Names.ContainsKey(m.Target.Id) && (m.Sender is null or Operator || Names.ContainsKey(m.Sender));

    private Owner Become(int partition, string node)
    {
        var lease = Real.Take(Store, partition, node, Lease) ?? throw new InvalidOperationException($"{node} could not take partition {partition}");
        var owner = new Owner(node, partition, lease, new Worker(node, partition, Store, Type, new WorkerOptions { Partitions = 4 }));
        Owners[partition] = owner;
        return owner;
    }

    /// <summary>Another process takes the partition once the owner's lease has run out. The old owner's memory is lost.</summary>
    public Owner Takeover(int partition, string why)
    {
        var old = Owners[partition];
        old.Alive = false;
        Now += (long)Lease.TotalMilliseconds + 1;
        var owner = Become(partition, $"n{partition}-{++_nodes}");
        Line($"takeover of partition {partition} {why}: {old.Node} (epoch {old.Epoch}) -> {owner.Node} (epoch {owner.Epoch}), memory empty");
        return owner;
    }

    /// <summary>The Worker's Reset after an unknown commit outcome (Worker.cs 476–502): the same process takes the partition again.</summary>
    private Owner Retake(int partition)
    {
        var old = Owners[partition];
        old.Alive = false;
        var owner = Become(partition, old.Node);
        Line($"  reset after the unknown outcome: {old.Node} retakes partition {partition} at epoch {owner.Epoch}, memory empty");
        return owner;
    }

    public void Seed(InstanceRow row, string? name = null)
    {
        Store.Instances[row.Id] = row;
        if (name is not null) Names[row.Id] = name;
    }

    /// <summary>Setup: the ticket rows a StartJob commit writes (Turns.cs 401), sent by a setup commit of the instance.</summary>
    public InboxKey SeedTicket(string actor, long creation, string key, long generation)
    {
        var id = Derive(actor, key, creation, generation);
        var ticket = new Message(id.Id, new InstanceRef(actor), Kinds.Job, Json.Write(new { Model = "rt", Key = key, Arguments = (string?)null }), actor,
            TargetCreation: creation, SenderCreation: creation).WithInboxScope(id.Scope);
        var result = Commit(new InstanceRef(actor).Partition, [new TurnCommit(actor, null, null, [], [], [ticket], [], [], null)], null, "setup ticket");
        if (!result.Landed) throw new InvalidOperationException("setup ticket refused: " + result.Error);
        return id;
    }

    /// <summary>
    /// The exact ticket key of a job (R5-B1, R5-m3): instance, creation, job key and <c>JobGeneration</c>. The built
    /// <c>Worker.TicketId</c> takes the first three (Worker.cs 295–296); the generation is carried by the proposed
    /// suffix <c>key.generation</c> inside the built length-prefixed key, which stays injective since the generation is
    /// the digits after the last dot. Generation 0 is the built three-part key.
    /// </summary>
    public static InboxKey Derive(string actor, string key, long creation, long generation) =>
        Real.Ticket(actor, generation > 0 ? FormattableString.Invariant($"{key}.{generation}") : key, creation);

    public void AddOutside(Message message)
    {
        if (!Real.Add(Store, message)) throw new InvalidOperationException("outside add refused: " + message.Id);
        _queue[Store.Messages[message.InboxKey].Seq] = message.InboxKey;
    }

    public bool Pending(InboxKey key) => Store.Messages.TryGetValue(key, out var row) && !row.Done;

    // ---- commits ---------------------------------------------------------------------------------------------------

    /// <summary>One commit of the partition's owner. The send cap is checked first; a hook may crash the owner around it.</summary>
    public CommitResult Commit(int partition, IReadOnlyList<TurnCommit> turns, Action? onLanded, string what, PageHook hook = PageHook.None)
    {
        var owner = Owners[partition];
        var sends = turns.Sum(t => t.Sent.Count);
        if (sends > SendCap)
        {
            Line($"  {what}: refused before the store, {sends} sends above the send cap {SendCap} (proposed, enforced by the harness)");
            return new(false, $"send cap ({sends} sends)", Failure.Definite, null);
        }
        var batch = new CommitBatch(partition, owner.Epoch, owner.Position, Lease, false, turns, [], []);
        if (hook == PageHook.CrashLost)
        {
            owner.Alive = false;
            Line($"  {what}: the owner {owner.Node} crashes before its commit reaches the store (unknown outcome: it did not land)");
            Takeover(partition, "after the crash");
            return new(false, "crash before the commit", Failure.Unknown, batch);
        }
        return Land(owner, batch, onLanded, what);
    }

    /// <summary>After a landed page has been reported: the takeover or the crash its schedule puts there.</summary>
    private void AfterLanded(PageHook hook, CommitResult result)
    {
        if (!result.Landed || hook is not (PageHook.CrashLanded or PageHook.TakeoverAfter)) return;
        var batch = result.Batch!;
        var owner = Owners[batch.Partition];
        if (hook == PageHook.TakeoverAfter) { Takeover(batch.Partition, "after this page"); return; }
        owner.Alive = false;
        Line($"  the owner {owner.Node} crashes before it learns that this commit landed (unknown outcome: it landed)");
        Takeover(batch.Partition, "after the crash");
        var stale = Land(owner, batch, null, "the dead owner's retry of the same batch, under its old epoch");
        StaleReplays.Add(stale.Landed);
    }

    /// <summary>The real commit. On landing, the proposed columns are written in the same transaction (<paramref name="onLanded"/>).</summary>
    public CommitResult Land(Owner owner, CommitBatch batch, Action? onLanded, string what)
    {
        Committed committed;
        try { committed = Real.Commit(Store, batch); }
        catch (StoreException e)
        {
            Line($"  {what}: StoreException ({e.Failure}): {e.Message}");
            return new(false, e.Message, e.Failure, batch);
        }
        if (!committed.Landed)
        {
            Line($"  {what}: not landed, the fence matched nothing (epoch {batch.Epoch}, position {batch.Position})");
            return new(false, "fence", null, batch);
        }
        if (Owners[batch.Partition] == owner) owner.Position = batch.Position + batch.Positions;
        var sent = batch.Turns.SelectMany(t => t.Sent).ToList();
        for (var i = 0; i < sent.Count; i++) { _queue[committed.Seqs[i]] = sent[i].InboxKey; Sent.Add(sent[i]); }
        LargestCommit = Math.Max(LargestCommit, sent.Count);
        onLanded?.Invoke();
        return new(true, null, null, batch);
    }

    /// <summary>
    /// A refused delivery stays in the inbox. After an unknown outcome the Worker resets and takes the partition again,
    /// and the row is delivered again; after <see cref="RetryLimit"/> attempts the run stops trying (the step bound).
    /// </summary>
    private void Failed(Owner owner, Message m, CommitResult result)
    {
        var n = _attempts[m.InboxKey] = _attempts.GetValueOrDefault(m.InboxKey) + 1;
        if (result.Failure == Failure.Unknown && owner.Alive && result.Error != "crash before the commit") Retake(owner.Partition);
        if (n >= RetryLimit) { Stuck.Add(m.InboxKey); Line($"  {m.Id}: {n} attempts, none landed; it stays pending (step bound)"); }
    }

    private static TurnCommit ToTurnCommit(TurnOutcome u, IReadOnlyList<InboxKey> cancels, IReadOnlyList<Message>? sends = null) => new(u.Instance, u.Row, u.Expected,
        [.. u.Consumed.Where(i => Real.Durable(i) && i.Message.KeepUntil is null).Select(i => i.Message.InboxKey), .. cancels],
        u.Consumed.Where(i => Real.Durable(i) && i.Message.KeepUntil is not null).Select(i => i.Message.InboxKey).ToList(),
        sends ?? u.Sent.Where(m => !u.Volatile.Contains(m.InboxKey)).ToList(), u.Subscribed, u.Unsubscribed, u.Journal, u.Unsubscribe,
        u.Held.Where(Real.Durable).Select(i => i.Message.InboxKey).ToList(), u.Delete);

    // ---- delivery --------------------------------------------------------------------------------------------------

    /// <summary>Deliver the lowest-seq row that is due: not held back, not given up, with a live owner. A live instance's job ticket is its driver's.</summary>
    public bool Step()
    {
        List<long>? gone = null;
        (Message M, long Seq)? next = null;
        foreach (var (seq, key) in _queue)
        {
            if (!Store.Messages.TryGetValue(key, out var row) || row.Done || row.Seq != seq) { (gone ??= []).Add(seq); continue; }
            if (HeldBack.Contains(key) || Stuck.Contains(key) || WithModel.Contains(key) || !Owners[row.Message.Target.Partition].Alive) continue;
            if (row.Message.Kind == Kinds.Job && Store.Instances.GetValueOrDefault(row.Message.Target.Id) is { Retired: false }) continue;
            next = (row.Message, seq);
            break;
        }
        gone?.ForEach(s => _queue.Remove(s));
        if (next is { } due) { Deliver(due.M, due.Seq); return true; }
        while (_continuations.Count > 0)
        {
            var (owner, actor) = _continuations[0];
            _continuations.RemoveAt(0);
            if (!owner.Alive || Owners[owner.Partition] != owner) { Line($"  {owner.Node}'s in-memory continuation of {Name(actor)}'s cascade died with it"); continue; }
            Discharge(owner, actor, null);
            return true;
        }
        return false;
    }

    public void Settle()
    {
        for (var guard = 0; Step(); guard++)
            if (guard > 2_000_000) throw new InvalidOperationException("the schedule did not settle");
    }

    /// <summary>Deliver one stored row now (a schedule step), if it is still there.</summary>
    public void DeliverNow(InboxKey key, string why)
    {
        Line(why);
        if (!Store.Messages.TryGetValue(key, out var row) || row.Done) { Line($"  {key.Id}: no such row in the store, nothing to deliver"); return; }
        Deliver(row.Message, row.Seq);
    }

    public void Deliver(Message m, long seq)
    {
        var id = m.Target.Id;
        var owner = Owners[m.Target.Partition];
        if (m.Kind == Kinds.Terminate) TerminatesDelivered[id] = TerminatesDelivered.GetValueOrDefault(id) + 1;
        var row = Store.Instances.GetValueOrDefault(id);
        if (row is null) { ConsumeOnly(owner, m, "no target: consumed unseen"); return; }
        if (m.InboxKey.Scope == CascadeScope) { Discharge(owner, id, m); return; }
        var error = Real.TargetError(row, m);
        if (error is not null) { RefusalPath(owner, row, m, seq, error); return; }
        if (DrainOpen.Contains(id) && m.Kind == Kinds.Terminate) { OpenDrainRefusal(owner, row, m, seq); return; }
        if (m.Kind == Kinds.Terminate && m.InboxKey == InboxKey.Issued(m.Id) && BindingApplies(row) && !ParentBound(m, row.Parent, row.ParentCreation, row.Creation))
        {
            Refusals.Add($"{m.Id}: parent binding");
            ConsumeOnly(owner, m, $"refused by the parent binding (proposed, R5-M5): Sender {Name(m.Sender!)}/{m.SenderCreation} is not the recorded parent {(row.Parent is null ? "null" : Name(row.Parent))}/{row.ParentCreation}; no effect, no cascade");
            return;
        }
        if (m.Kind == Kinds.Terminate && m.InboxKey == InboxKey.Issued(m.Id) && !BindingApplies(row) && !ParentBound(m, row.Parent, row.ParentCreation, row.Creation) && Shown(m))
            Line($"  {m.Id} from {Name(m.Sender!)}/{m.SenderCreation} is not from the recorded parent {(row.Parent is null ? "null" : Name(row.Parent))}/{row.ParentCreation}; this rule set has no binding at {(row.Status == InstanceStatus.Running ? "a running" : "an ended")} recipient");
        var outcome = Real.Command(Type, owner.Worker, row, m, seq);
        if (outcome is null)
        {
            WithModel.Add(m.InboxKey);
            if (Shown(m)) Line($"deliver {m.Id} ({m.Kind}) to {Name(id)}: WorkflowInstance.Command hands it to the model; no model turn is modelled, so it stays in the inbox");
            return;
        }
        if (m.Kind == Kinds.Terminate && outcome.Row is not null && !outcome.Delete) { CommitTerminate(owner, row, m, seq, outcome, "built terminate (WorkflowInstance.cs 179-203)"); return; }
        CommitOutcome(owner, row, m, outcome);
    }

    /// <summary>R5-M5: which recipient states check the parent binding. Parked and draining recipients check it under every rule set (#4756).</summary>
    private bool BindingApplies(InstanceRow row) => row.Status == InstanceStatus.Running ? Rules.BindingRunning : (row.Children is not null || row.Jobs is not null) && Rules.BindingEnded;

    public static bool ParentBound(Message m, string? parent, long parentCreation, long creation) =>
        parent is not null && m.InboxKey == InboxKey.Issued(m.Id) && m.Sender == parent && m.SenderCreation == parentCreation && m.TargetCreation == creation;

    private void ConsumeOnly(Owner owner, Message m, string why)
    {
        var result = Commit(owner.Partition, [new TurnCommit(m.Target.Id, null, null, [m.InboxKey], [], [], [], [], null)], null, $"consume {m.Id}");
        if (Shown(m)) Line($"deliver {m.Id} ({m.Kind}) to {Name(m.Target.Id)}: {why}");
        if (!result.Landed) Failed(owner, m, result);
    }

    /// <summary>The Worker's refusal path (Worker.cs 1050–1055), which a parent-bound terminate at a marker extends with the cascade.</summary>
    private void RefusalPath(Owner owner, InstanceRow row, Message m, long seq, string error)
    {
        Refusals.Add($"{m.Id}: {error}");
        if (error == "RetiredCreation" && m.Kind == Kinds.Terminate && Obligations.TryGetValue(row.Id, out var ob) && ob.State == "Held" && ob.Open)
        {
            if (ParentBound(m, ob.Parent, ob.ParentCreation, row.Creation)) { Line($"deliver {m.Id} (terminate from {Name(m.Sender!)}) to {Name(row.Id)}'s marker: refused, RetiredCreation; parent-bound, so the refusal path composes the cascade"); MarkerCascade(owner, row, m, ob); return; }
            Line($"  {m.Id}: not from the recorded parent {ob.Parent}/{ob.ParentCreation}: plain refusal, no cascade");
        }
        var outcome = Real.Refuse(row.Id, row, Real.Delivery(m, seq), error);
        var result = Commit(owner.Partition, [ToTurnCommit(outcome, [])], null, $"refuse {m.Id}");
        if (Shown(m) || m.Kind == Kinds.Job) Line($"deliver {m.Id} ({m.Kind}) to {Name(row.Id)}{(row.Retired ? "'s marker" : "")}: refused, {error}, and consumed (Worker.cs 1050-1055, CreationProtocol.Refuse){(result.Landed ? "" : "; the commit did not land")}");
        if (result.Landed && m.Kind == Kinds.Job) owner.Settled.Add(m.InboxKey);
        if (!result.Landed) Failed(owner, m, result);
    }

    /// <summary>#4756's open-drain refusal (J-1): a terminate with authority is refused as "resolution open" and its commit does the subtree part of the built terminate of an ended row.</summary>
    private void OpenDrainRefusal(Owner owner, InstanceRow row, Message m, long seq)
    {
        if (!ParentBound(m, row.Parent, row.ParentCreation, row.Creation)) { Refusals.Add($"{m.Id}: resolution open (no cascade)"); ConsumeOnly(owner, m, "refused, resolution open, no cascade"); return; }
        Refusals.Add($"{m.Id}: resolution open (with cascade)");
        Line($"deliver {m.Id} to {Name(row.Id)} while its delete drain is open: refused, resolution open; the refusing commit runs the built terminate of the ended row (WorkflowInstance.cs 63-64, 158, 179-198)");
        var outcome = Real.Command(Type, owner.Worker, row, m, seq) ?? throw new InvalidOperationException("an ended row with a registry takes the control path");
        CommitTerminate(owner, row, m, seq, outcome, "refusing commit (J-1)");
    }

    /// <summary>
    /// A terminate's commit: the real outcome's row, consumed input and notice, with the cancels decided by the rule set
    /// and the child terminates paged. The page's ids are the built Terminate's (origin, the next revision, ordinal);
    /// the harness checks that.
    /// </summary>
    private void CommitTerminate(Owner owner, InstanceRow row, Message m, long seq, TurnOutcome outcome, string what)
    {
        var ob = new Obligation
        {
            Actor = row.Id, Creation = row.Creation, Parent = row.Parent, ParentCreation = row.ParentCreation,
            Children = Real.ReadRegistry(row.Children), Jobs = Real.ReadJobs(row.Jobs).Select(k => (k, JobGeneration.GetValueOrDefault((row.Id, k)))).ToList(),
            IdRevision = outcome.Row!.Revision, State = "Discharging",
        };
        var page = Compose(owner, ob, 0);
        var built = outcome.Sent.Where(s => s.Kind == Kinds.Terminate).ToList();
        foreach (var s in page.Sends.Where(s => s.Kind == Kinds.Terminate))
            if (!built.Any(b => b.InboxKey == s.InboxKey && b.Target == s.Target && b.TargetCreation == s.TargetCreation && b.Sender == s.Sender && b.SenderCreation == s.SenderCreation)) IdMismatches++;
        var notices = outcome.Sent.Where(s => s.Kind != Kinds.Terminate).ToList();
        var turn = ToTurnCommit(outcome, page.Consumed, [.. page.Sends, .. notices]);
        var shown = Names.ContainsKey(row.Id);
        if (shown) Line($"deliver {m.Id} (terminate from {Name(m.Sender ?? "?")}) to {Name(row.Id)}: the {what}; {row.Status} -> {outcome.Row.Status}, {ob.Children.Count} children, {ob.Jobs.Count} jobs; the built outcome cancels [{string.Join(", ", outcome.Cancelled.Select(c => c.Id))}]");
        var hook = Hook?.Invoke(row.Id, page.Number) ?? PageHook.None;
        var result = Commit(owner.Partition, [turn], () => Applied(owner, ob, page, m), $"{Name(row.Id)}'s {what}", hook);
        if (result.Landed)
        {
            if (shown) Line($"  landed: page {page.Number} [{page.From}, {page.To}) of {ob.Entries} entries; {Describe(page)}{(notices.Count > 0 ? $"; notice {string.Join(", ", notices.Select(n => n.Id))} to {Name(row.Parent!)}" : "")}");
            AfterLanded(hook, result);
            AfterPage?.Invoke(row.Id, page.Number);
            return;
        }
        if (result.Error?.StartsWith("send cap", StringComparison.Ordinal) == true && row.Status == InstanceStatus.Running)
        {
            // The owner card: a turn above the send cap fails and parks.
            var park = Real.Park(Type, owner.Worker, row, m, seq, "send cap");
            var parked = Commit(owner.Partition, [ToTurnCommit(park, [])], null, $"{Name(row.Id)}'s Park");
            Line($"  the turn fails and parks (owner card): {Name(row.Id)} {row.Status} -> {park.Row?.Status}, {m.Id} consumed, its {ob.Children.Count} children left running{(parked.Landed ? "" : "; the Park did not land")}");
            return;
        }
        if (result.Error != "crash before the commit") Failed(owner, m, result);
    }

    private void CommitOutcome(Owner owner, InstanceRow row, Message m, TurnOutcome outcome)
    {
        Action? onLanded = null;
        string? note = null;
        if (outcome.Delete && Obligations.TryGetValue(row.Id, out var ob) && ob.Open)
            onLanded = () =>
            {
                if (Rules.DeleteDropsDischarging) { Obligations.Remove(row.Id); note = $"  the delete drops {Name(row.Id)}'s Discharging obligation (mutation)"; }
                else { ob.MarkerRevision = outcome.Row?.Revision ?? 0; note = $"  the obligation stays on the marker, still {ob.State}: {ob}"; }
            };
        var result = Commit(owner.Partition, [ToTurnCommit(outcome, [])], onLanded, $"{m.Kind} at {Name(row.Id)}");
        if (Shown(m))
            Line($"deliver {m.Id} ({m.Kind}) to {Name(row.Id)}: WorkflowInstance.Command -> {(outcome.Delete ? "delete, retirement marker (CreationProtocol.Retire)" : outcome.Row is { } r ? $"row at revision {r.Revision}, {r.Status}, children {r.Children ?? "null"}" : "consumed with no change")}{(result.Landed ? "" : "; did not land")}");
        if (note is not null) Line(note);
        if (!result.Landed) Failed(owner, m, result);
    }

    // ---- the cascade (proposed) ------------------------------------------------------------------------------------

    /// <summary>
    /// The marker's cascade (RM-1, and R5-B2's Held -> Discharging): the refusing commit consumes the parent's
    /// terminate and sends the first page. The built <c>Validate</c> refuses a retired-to-retired row write, so the
    /// marker's revision + 1 is kept with the proposed columns; each report prints that refusal.
    /// </summary>
    private void MarkerCascade(Owner owner, InstanceRow marker, Message t, Obligation ob)
    {
        ob.IdRevision = ob.MarkerRevision + 1;
        var page = Compose(owner, ob, 0);
        var turn = new TurnCommit(marker.Id, null, null, [t.InboxKey, .. page.Consumed], [], page.Sends, [], [], null);
        Line($"  built CreationProtocol.Validate on the marker's write at revision {marker.Revision + 1}: {Real.Validate(new TurnCommit(marker.Id, marker with { Revision = marker.Revision + 1 }, marker.Revision, [], [], [], [], [], null), marker)} (the K3 change widens it; here the marker revision lives with the proposed columns)");
        var hook = Hook?.Invoke(marker.Id, page.Number) ?? PageHook.None;
        var result = Commit(owner.Partition, [turn], () => Applied(owner, ob, page, t), $"the refusing commit at {Name(marker.Id)}'s marker", hook);
        if (result.Landed) { Line($"  landed: page {page.Number} [{page.From}, {page.To}) of {ob.Entries} entries, {page.Consumed.Count + 1} counted inputs; {Describe(page)}"); AfterLanded(hook, result); AfterPage?.Invoke(marker.Id, page.Number); }
        else if (result.Error != "crash before the commit") Failed(owner, t, result);
    }

    /// <summary>A page driven by a <c>kernel.cascade</c> control (or, under <c>no-next-control</c>, by the owner's memory).</summary>
    private void Discharge(Owner owner, string actor, Message? control)
    {
        var row = Store.Instances[actor];
        if (!Obligations.TryGetValue(actor, out var ob) || !ob.Open || control is not null && (control.Sender != actor || control.SenderCreation != row.Creation))
        {
            if (control is not null) ConsumeOnly(owner, control, "a stale cascade control: consumed with no change");
            return;
        }
        var from = Rules.DurableCursor ? ob.Cursor : owner.MemoryCursor.GetValueOrDefault(actor);
        var page = Compose(owner, ob, from);
        var write = row.Retired ? null : row with { Revision = row.Revision + 1 };
        var turn = new TurnCommit(actor, write, row.Retired ? null : row.Revision, [.. control is null ? [] : new[] { control.InboxKey }, .. page.Consumed], [], page.Sends, [], [], null);
        var hook = Hook?.Invoke(actor, page.Number) ?? PageHook.None;
        var where = row.Retired ? "marker" : "row";
        var result = Commit(owner.Partition, [turn], () => Applied(owner, ob, page, control), $"page {page.Number} of {Name(actor)}'s cascade", hook);
        if (result.Landed)
        {
            Line($"page {page.Number} at {Name(actor)}'s {where} ({owner.Node}, epoch {owner.Epoch}, {(control is null ? "continued from memory" : control.Id)}): [{page.From}, {page.To}) of {ob.Entries}; {Describe(page)}");
            AfterLanded(hook, result);
            AfterPage?.Invoke(actor, page.Number);
        }
        else if (control is not null && result.Error != "crash before the commit") Failed(owner, control, result);
    }

    /// <summary>The proposed columns of a landed page, in its transaction; then the owner's memory.</summary>
    private void Applied(Owner owner, Obligation ob, Page page, Message? consumed)
    {
        if (ob.Entries == 0) return;
        Pages++;
        Obligations[ob.Actor] = ob;
        if (Store.Instances.GetValueOrDefault(ob.Actor) is { Retired: true }) ob.MarkerRevision++;
        if (page.More) { ob.State = "Discharging"; if (Rules.DurableCursor) ob.Cursor = page.To; }
        else { ob.State = "Cleared"; ob.Cursor = page.To; }
        if (page.SentControl) ob.NextControl++;
        foreach (var key in page.KeptCleared) KeptBy.Remove(key);
        CascadeCommits.Add((ob.Actor, page.Number, [.. consumed is null ? [] : new[] { consumed.InboxKey }, .. page.Consumed], owner.Epoch));
        owner.MemoryCursor[ob.Actor] = page.To;
        if (page.More && !Rules.NextControl) _continuations.Add((owner, ob.Actor));
    }

    private Page Compose(Owner owner, Obligation ob, int from)
    {
        var size = Rules.Paged ? Rules.PCascade : int.MaxValue;
        var to = (int)Math.Min(ob.Entries, (long)from + size);
        var page = new Page(Rules.Paged ? from / Rules.PCascade + 1 : 1, from, to, [], [], [], [], to < ob.Entries, false);
        var origin = Real.Origin(ob.Actor, ob.Creation);
        for (var i = from; i < to; i++)
        {
            if (i < ob.Jobs.Count) { Cancel(owner, ob, ob.Jobs[i], page); continue; }
            var index = i - ob.Jobs.Count;
            var child = ob.Children[index];
            page.Sends.Add(new Message(FormattableString.Invariant($"{origin}#{ob.IdRevision}.{index:D4}"), new InstanceRef(child.Id), Kinds.Terminate, Sender: ob.Actor,
                TargetCreation: child.Creation > 0 ? child.Creation : null, SenderCreation: ob.Creation).AsIssued());
        }
        if (page.More && Rules.NextControl)
        {
            page.Sends.Add(new Message(FormattableString.Invariant($"{origin}#cascade.{ob.NextControl}"), new InstanceRef(ob.Actor), CascadeKind,
                Json.Write(new { Page = ob.NextControl }), ob.Actor, TargetCreation: ob.Creation, SenderCreation: ob.Creation).WithInboxScope(CascadeScope));
            page = page with { SentControl = true };
        }
        return page;
    }

    /// <summary>
    /// R5-B1. Reviewed: every listed ticket is a counted consume. Repaired: the owner decides presence under its own
    /// fence, from the store's row at its epoch: pending is consumed (counted 1) with the in-process cancel, done and
    /// kept has its kept state cleared, absent is nothing.
    /// </summary>
    private void Cancel(Owner owner, Obligation ob, (string Key, long Generation) job, Page page)
    {
        var origin = Rules.ForeignDerivation && ForeignActor is { } foreign ? foreign : ob.Actor;
        var keys = Rules.GenerationBlind
            ? Store.Messages.Values.Where(r => r.Message.Kind == Kinds.Job && r.Message.Target.Id == origin && r.Message.TargetCreation == ob.Creation && JobKey(r.Message) == job.Key)
                .OrderBy(r => r.Seq).Select(r => r.Message.InboxKey).ToList()
            : [Derive(origin, job.Key, ob.Creation, job.Generation)];
        foreach (var key in keys)
        {
            if (Rules.CountedCancel) { page.Consumed.Add(key); page.Notes.Add($"{key.Id}: counted consume"); continue; }
            var row = Store.Messages.GetValueOrDefault(key);
            var present = Rules.PresenceFromMemory ? !owner.Settled.Contains(key) : row is not null;
            var source = Rules.PresenceFromMemory ? "owner memory" : $"store row at epoch {owner.Epoch}";
            if (!present) page.Notes.Add($"{key.Id}: absent ({source}), nothing");
            else if (!Rules.PresenceFromMemory && row!.Done) { page.KeptCleared.Add(key); page.Notes.Add($"{key.Id}: done and kept ({KeptBy.GetValueOrDefault(key, "no KeptBy")}), kept state cleared"); }
            else { page.Consumed.Add(key); page.Notes.Add($"{key.Id}: pending ({source}), consumed (counted 1), in-process cancel"); }
        }
    }

    public static string? JobKey(Message ticket) =>
        ticket.Payload is null ? null : System.Text.Json.JsonDocument.Parse(ticket.Payload).RootElement.GetProperty("Key").GetString();

    private string Describe(Page page)
    {
        var terminates = page.Sends.Count(s => s.Kind == Kinds.Terminate);
        var shown = page.Sends.Where(s => s.Kind == Kinds.Terminate).Take(3).Select(s => $"{Name(s.Target.Id)} as {s.Id}");
        return $"{terminates} child terminates [{string.Join(", ", shown)}{(terminates > 3 ? ", ..." : "")}]" +
            (page.Notes.Count > 0 ? $"; cancels: {string.Join("; ", page.Notes)}" : "") +
            (page.SentControl ? $"; next control {page.Sends[^1].Id}" : page.More ? "; no control sent" : "; obligation cleared");
    }

    // ---- the delete drain's Complete (proposed retirement with obligation) -----------------------------------------

    /// <summary>
    /// A's delete drain completes: the one notice to the parent (real <c>Finished</c>), the real <c>Retire</c> marker,
    /// and the proposed obligation: a Discharging one moves to the marker unchanged; otherwise a child's marker holds
    /// the registry and the jobs with their generations (Held). The proposed widening of <c>Validate</c> at retirement
    /// refuses a job pair the registry does not hold (RM-1, R5-m3).
    /// </summary>
    public bool Complete(string actor, List<CreationAddress>? extraChildren = null, Func<List<(string Key, long Generation)>, List<(string Key, long Generation)>>? alterJobs = null)
    {
        var row = Store.Instances[actor];
        var owner = Owners[new InstanceRef(actor).Partition];
        var notice = Real.Finished(row);
        var marker = Real.Retire(row);
        var registry = Real.ReadJobs(row.Jobs).Select(k => (k, JobGeneration.GetValueOrDefault((actor, k)))).ToList();
        Obligation? ob = null;
        if (Obligations.TryGetValue(actor, out var open) && open.Open) { ob = open; Line($"{Name(actor)}'s delete drain Complete: the Discharging obligation moves to the marker as it is"); }
        else if (row.Parent is not null)
        {
            var jobs = alterJobs?.Invoke([.. registry]) ?? registry;
            var children = Real.ReadRegistry(row.Children);
            if (extraChildren is not null) { children.AddRange(extraChildren); Line($"fixture: the retirement handoff also lists [{string.Join(", ", extraChildren.Select(c => $"{Name(c.Id)}/{c.Creation}"))}] (RM-1's negative fixture; Validate at retirement checks no membership)"); }
            var off = jobs.Where(j => !registry.Contains(j)).ToList();
            if (off.Count > 0 || jobs.Count > registry.Count)
            {
                var text = $"the retirement list names [{string.Join(", ", off.Select(j => $"{j.Key}@g{j.Generation}"))}], not in the registry [{string.Join(", ", registry.Select(j => $"{j.Item1}@g{j.Item2}"))}]";
                if (!Rules.AcceptOffRegistry)
                {
                    RetirementRefusals.Add(text);
                    Line($"{Name(actor)}'s delete drain Complete: refused as a definite error by the proposed retirement check: {text}; {Name(actor)} stays unretired");
                    return false;
                }
                Line($"  accepted (mutation accept-off-registry): {text}");
            }
            ob = new Obligation { Actor = actor, Creation = row.Creation, Parent = row.Parent, ParentCreation = row.ParentCreation, Children = children, Jobs = jobs };
        }
        var turn = new TurnCommit(actor, marker, row.Revision, [], [], [notice], [], [], Json.Write(new[] { "deleted" }), Unsubscribe: true, Delete: true);
        var result = Commit(owner.Partition, [turn], () =>
        {
            DrainOpen.Remove(actor);
            if (ob is { Open: true }) { ob.MarkerRevision = marker.Revision; Obligations[actor] = ob; }
        }, $"{Name(actor)}'s delete drain Complete");
        Line($"{Name(actor)}'s delete drain Complete {(result.Landed ? "landed" : "did not land")}: notice {notice.Id} ({row.Status}) to {Name(row.Parent ?? "none")}; retirement marker at revision {marker.Revision}; obligation {(ob is { Open: true } ? ob.ToString() : "none")}");
        return result.Landed;
    }

    // ---- observation -----------------------------------------------------------------------------------------------

    public string Status(string id) => Store.Instances.GetValueOrDefault(id) switch
    {
        null => "absent",
        { Retired: true } => "retired marker",
        var row => row.Status.ToString(),
    };

    public int SentTo(string id, string kind, string? sender = null) => Sent.Count(m => m.Target.Id == id && m.Kind == kind && (sender is null || m.Sender == sender));

    public int OpenObligations => Obligations.Values.Count(o => o.Open);

    /// <summary>Checks every run makes.</summary>
    public void CommonChecks()
    {
        Report.Expect("the store's ghost (I9: a counted input missing at the current epoch, InMemoryStore.cs 187-192)",
            Store.Ghost.Count == 0 ? "none" : string.Join(" | ", Store.Ghost), "none");
        Report.Expect("cascade child terminates whose id differs from the built Terminate's composition", IdMismatches, 0);
        Line($"commits through InMemoryStore.CommitAsync: {Real.Calls.GetValueOrDefault("InMemoryStore.CommitAsync")}, terminate and cascade commits with entries (every actor) {Pages}, largest commit {LargestCommit} sends (cap {SendCap})");
    }
}
