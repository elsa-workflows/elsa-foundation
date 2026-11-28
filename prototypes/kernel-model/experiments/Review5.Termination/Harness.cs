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
    private int _nodes, _triggers;
    public const int RetryLimit = 3;

    // Proposed columns, beside the real rows.
    public readonly Dictionary<string, Obligation> Obligations = new(StringComparer.Ordinal);
    public readonly Dictionary<(string Actor, string Key), long> JobGeneration = [];
    public readonly Dictionary<InboxKey, string> KeptBy = [];
    public readonly HashSet<string> DrainOpen = new(StringComparer.Ordinal);
    /// <summary>The cascade origin mark (R5-M5, proposed): a flag on an issued-origin message that only kernel composition sets.</summary>
    public readonly HashSet<InboxKey> Marks = [];
    /// <summary>Rows <c>ProtectsDeletion</c> protects (a fixture: the stub model has no metadata), for ThenDelete's kept variant.</summary>
    public readonly HashSet<string> Protects = new(StringComparer.Ordinal);

    // What the run observed.
    public readonly List<Message> Sent = [];
    public readonly Dictionary<string, int> TerminatesDelivered = new(StringComparer.Ordinal);
    public readonly List<string> Refusals = [];
    public readonly List<(string Actor, int Page, IReadOnlyList<InboxKey> Consumed, long Epoch)> CascadeCommits = [];
    public readonly List<bool> StaleReplays = [];
    public readonly List<string> RetirementRefusals = [];
    public int Pages, IdMismatches, CancelKeyMismatches, LargestCommit;
    public readonly Dictionary<string, int> FirstPageJobEntries = new(StringComparer.Ordinal);
    public readonly List<string> StartRefusals = [], Births = [];
    public readonly List<string> ThenDeleteShapes = [];
    public readonly List<(string Actor, int Page, InboxKey? Control)> LostPages = [];   // pages whose commit a crash kept from the store
    public readonly HashSet<InboxKey> ModelSends = [];   // every send a model turn attempted, landed or not (provenance)
    public readonly List<string> ExpectedGhost = [];   // I9 entries a schedule provokes on purpose (a redelivered, consumed row)
    public Action<string>? AfterDelete;               // (actor) -> a scheduled step after a delete landed
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
        var ticket = ModeledTicket.Issue(new Message(id.Id, new InstanceRef(actor), Kinds.Job,
            Json.Write(new { Model = "rt", Key = key, Arguments = (string?)null }), actor,
            TargetCreation: creation, SenderCreation: creation).WithInboxScope(id.Scope));
        var result = Commit(new InstanceRef(actor).Partition, [new TurnCommit(actor, null, null, [], [], [ticket], [], [], null)], null, "setup ticket");
        if (!result.Landed) throw new InvalidOperationException("setup ticket refused: " + result.Error);
        return id;
    }

    /// <summary>
    /// The exact ticket key of a job (R5-B1, R5-m3): instance, creation, job key and <c>JobGeneration</c>. The built
    /// <c>Worker.TicketId</c> takes the first three (Worker.cs 295–296), and generation 0 keeps exactly that key. A
    /// positive generation appends <c>#g{generation}</c> after the built id (proposed). The built id ends with the job
    /// key under its own length prefix (<c>!{length}_{key}</c>), so nothing a key spells reaches past it: the two forms,
    /// and any two generations, never meet. The scope is the built one.
    /// </summary>
    public static InboxKey Derive(string actor, string key, long creation, long generation)
    {
        var built = Real.Ticket(actor, key, creation);
        return generation > 0 ? new InboxKey(FormattableString.Invariant($"{built.Id}#g{generation}"), built.Scope) : built;
    }

    /// <summary>This harness's earlier proposed derivation, the generation inside the length-prefixed key (<c>key.generation</c>): <c>(j.1, 0)</c> and <c>(j, 1)</c> meet. The reviewed docs state no derivation.</summary>
    public static InboxKey DeriveInsideKey(string actor, string key, long creation, long generation) =>
        Real.Ticket(actor, generation > 0 ? FormattableString.Invariant($"{key}.{generation}") : key, creation);

    public void AddOutside(Message message)
    {
        if (!Real.Add(Store, message)) throw new InvalidOperationException("outside add refused: " + message.Id);
        var stored = message.AsOrdinary().InboxKey;   // AddAsync stores every outside add under the ordinary key (InMemoryStore.cs 231)
        _queue[Store.Messages[stored].Seq] = stored;
    }

    public bool Pending(InboxKey key) => Store.Messages.TryGetValue(key, out var row) && !row.Done;

    // ---- commits ---------------------------------------------------------------------------------------------------

    /// <summary>One commit of the partition's owner. The send cap is checked first; a hook may crash the owner around it.</summary>
    public CommitResult Commit(int partition, IReadOnlyList<TurnCommit> turns, Action? onLanded, string what, PageHook hook = PageHook.None,
        IReadOnlyCollection<InboxKey>? marks = null, bool kernel = true)
    {
        var owner = Owners[partition];
        var sends = turns.Sum(t => t.Sent.Count);
        if (sends > SendCap)
        {
            Line($"  {what}: refused before the store, {sends} sends above the send cap {SendCap} (proposed, enforced by the harness)");
            return new(false, $"send cap ({sends} sends)", Failure.Definite, null);
        }
        if (CommitRefusal(turns, marks ?? [], kernel) is { } refusal)
        {
            Line($"  {what}: refused before the store as a definite error: {refusal}");
            return new(false, refusal, Failure.Definite, null);
        }
        if (marks is { Count: > 0 })
        {
            var inner = onLanded;
            onLanded = () => { Marks.UnionWith(marks); inner?.Invoke(); };
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

    /// <summary>
    /// The commit's checks on composed terminates (proposed). Under every rule set, a <c>kernel.terminate</c> a turn
    /// sends carries the committing actor's own id and creation as Sender and SenderCreation (observability, "An actor
    /// type's composed send is checked"). With <see cref="Rules.CommitChecksMark"/>, a marked message must be a
    /// <c>kernel.terminate</c> from the committing row that the kernel composed: no model turn sets the mark.
    /// </summary>
    private string? CommitRefusal(IReadOnlyList<TurnCommit> turns, IReadOnlyCollection<InboxKey> marks, bool kernel)
    {
        foreach (var turn in turns)
        {
            var creation = turn.Row?.Creation ?? Store.Instances.GetValueOrDefault(turn.Instance)?.Creation ?? 0;
            foreach (var m in turn.Sent.Where(m => m.Kind == Kinds.Terminate))
                if (m.Sender != turn.Instance || m.SenderCreation != creation)
                    return $"the composed terminate {m.Id} names Sender {Name(m.Sender ?? "null")}/{m.SenderCreation}, not the committing {Name(turn.Instance)}/{creation}";
            if (!Rules.CommitChecksMark) continue;
            foreach (var m in turn.Sent.Where(m => marks.Contains(m.InboxKey)))
            {
                if (!kernel) return $"a model turn's send {m.Id} carries the cascade origin mark, which no turn method sets";
                if (m.Kind != Kinds.Terminate || m.InboxKey != InboxKey.Issued(m.Id)) return $"the marked {m.Id} is no issued kernel.terminate";
            }
        }
        if (Rules.CommitChecksMark && marks.FirstOrDefault(k => !turns.Any(t => t.Sent.Any(m => m.InboxKey == k))) is { Id: not null } stray)
            return $"the mark names {stray.Id}, which this batch does not send";
        return null;
    }

    /// <summary>
    /// A model's turn at <paramref name="actor"/>, triggered by an outside input: the real <c>DurableTurn</c> composes its
    /// sends, sealed at the next revision. <paramref name="mark"/> picks sends to carry the mark (a forgery).
    /// </summary>
    public CommitResult ModelTurn(string actor, Action<DurableTurn> compose, Func<Message, bool>? mark, string what)
    {
        var trigger = new Message($"go-{++_triggers}", new InstanceRef(actor), "app.go", Sender: Operator, TargetCreation: Store.Instances[actor].Creation);
        AddOutside(trigger);
        HeldBack.Add(trigger.InboxKey);
        var row = Store.Instances[actor];
        var owner = Owners[new InstanceRef(actor).Partition];
        var turn = Real.ModelTurn(owner.Worker, row, trigger, Store.Messages[trigger.InboxKey].Seq);
        compose(turn);
        var unit = turn.Seal(turn.Outcome(InstanceStatus.Running), row with { Revision = turn.Revision }, null, null);
        var commit = ToTurnCommit(unit, []);
        var marks = mark is null ? [] : commit.Sent.Where(mark).Select(m => m.InboxKey).ToList();
        ModelSends.UnionWith(commit.Sent.Select(m => m.InboxKey));
        var result = Commit(owner.Partition, [commit], null, what, marks: marks, kernel: false);
        Line($"{what}: sends [{string.Join(", ", commit.Sent.Select(m => $"{m.Kind} {m.Id} to {Name(m.Target.Id)} from {Name(m.Sender ?? "null")}/{m.SenderCreation}{(marks.Contains(m.InboxKey) ? ", marked" : "")}"))}]; {(result.Landed ? "landed" : "not landed")}");
        return result;
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
        if (m.Kind == Kinds.Start && m.Start is not null) { StartAt(owner, row, m, seq); return; }
        var error = Real.TargetError(row, m);
        if (error is not null) { RefusalPath(owner, row, m, seq, error); return; }
        if (DrainOpen.Contains(id) && m.Kind == Kinds.Terminate) { OpenDrainRefusal(owner, row, m, seq); return; }
        var marked = Marks.Contains(m.InboxKey);
        if (m.Kind == Kinds.Terminate && m.InboxKey == InboxKey.Issued(m.Id) && (marked || Rules.BindUnmarked) && BindingApplies(row) && !ParentBound(m, row.Parent, row.ParentCreation, row.Creation))
        {
            Refusals.Add($"{m.Id}: parent");
            ConsumeOnly(owner, m, $"refused, reason parent (proposed, R5-M5): {(marked ? "a marked" : "an unmarked")} terminate from {Name(m.Sender!)}/{m.SenderCreation}, not the recorded parent {(row.Parent is null ? "null" : Name(row.Parent))}/{row.ParentCreation}; no effect, no cascade");
            return;
        }
        if (m.Kind == Kinds.Terminate && m.InboxKey == InboxKey.Issued(m.Id) && !ParentBound(m, row.Parent, row.ParentCreation, row.Creation) && Shown(m))
            Line($"  {m.Id} ({(marked ? "marked" : "unmarked")}) from {Name(m.Sender!)}/{m.SenderCreation} is not from the recorded parent {(row.Parent is null ? "null" : Name(row.Parent))}/{row.ParentCreation}; under this rule set it is not parent-bound at {(row.Status == InstanceStatus.Running ? "a running" : "an ended")} recipient");
        if (ThenDelete(m) && row.Status != InstanceStatus.Running && (row.Children is not null || row.Jobs is not null) && !Rules.EndedThenDeleteCascades)
        {
            // The reviewed text: a ThenDelete reaching a run that has already ended takes the qualified-delete path
            // (data lifecycle, "Terminate, then delete"). That is the built delete (WorkflowInstance.cs 161–166), which
            // terminates none of the children. A child's marker keeps the Held obligation as at retirement
            // (data-lifecycle.md 94–95 at 0a1bb5e0c), so the subtree lives until the parent's terminate discharges it.
            Line($"  {m.Id} carries ThenDelete and {Name(row.Id)} has ended: the qualified-delete path, the built delete of the ended row");
            var held = row.Parent is null ? null : new Obligation
            {
                Actor = row.Id, Creation = row.Creation, Parent = row.Parent, ParentCreation = row.ParentCreation, Children = Real.ReadRegistry(row.Children),
                Jobs = Real.ReadJobs(row.Jobs).Select(k => (k, JobGeneration.GetValueOrDefault((row.Id, k)))).ToList(),
            };
            CommitOutcome(owner, row, m, Real.Command(Type, owner.Worker, row, m with { Kind = Kinds.Delete }, seq)!, held);
            return;
        }
        var outcome = Real.Command(Type, owner.Worker, row, m, seq);
        if (outcome is null)
        {
            WithModel.Add(m.InboxKey);
            if (Shown(m)) Line($"deliver {m.Id} ({m.Kind}) to {Name(id)}: WorkflowInstance.Command hands it to the model; no model turn is modelled, so it stays in the inbox");
            return;
        }
        if (m.Kind == Kinds.Terminate && outcome.Row is not null && !outcome.Delete)
        {
            // ThenDelete starts the terminate's cascade at a running row, or at an ended row with a registry or jobs
            // (the third Validate shape); an ended row with neither takes the Command path above as consumed input.
            var thenDelete = ThenDelete(m) && (row.Status == InstanceStatus.Running || row.Children is not null || row.Jobs is not null);
            if (thenDelete && Protects.Contains(row.Id)) Line($"  {m.Id} carries ThenDelete; ProtectsDeletion protects {Name(row.Id)}, so it is terminated and kept");
            CommitTerminate(owner, row, m, seq, outcome, thenDelete && !Protects.Contains(row.Id) ? "built terminate, then the delete in the same commit (ThenDelete)" : "built terminate (WorkflowInstance.cs 179-203)",
                thenDelete && !Protects.Contains(row.Id));
            return;
        }
        CommitOutcome(owner, row, m, outcome);
    }

    /// <summary>
    /// R5-M5: at which states a marked terminate is parent-bound. A recipient whose delete drain is open, and a marker,
    /// check the binding under every rule set (#4756, RM-1). A parked recipient with no drain open is not modelled
    /// apart: its row is Faulted, so this treats it as an ended row.
    /// </summary>
    private bool BindingApplies(InstanceRow row) => row.Status == InstanceStatus.Running ? Rules.BindingRunning : (row.Children is not null || row.Jobs is not null) && Rules.BindingEnded;

    /// <summary>An operator's terminate that also deletes (proposed payload <c>{"ThenDelete":true}</c>, data lifecycle "Terminate, then delete").</summary>
    private static bool ThenDelete(Message m) => m.InboxKey.Scope == 0 && m.Payload is { } p && p.Contains("\"ThenDelete\":true", StringComparison.Ordinal);

    /// <summary>
    /// A start at an id (Worker.cs 1076–1080 routes it to the real <c>StartError</c>). The proposed defence in depth
    /// refuses it with ObligationOutstanding while the id's marker holds an open obligation. An accepted start writes
    /// the successor row through the real commit (the start's model turn is not modelled).
    /// </summary>
    private void StartAt(Owner owner, InstanceRow row, Message m, long seq)
    {
        var error = Real.StartError(row, m.Start!, m.Kind, m.Payload, m.InboxKey);
        if (error is null && Obligations.TryGetValue(row.Id, out var ob) && ob.Open) error = "ObligationOutstanding";
        if (error is not null)
        {
            StartRefusals.Add($"{m.Id}: {error}");
            var refused = Commit(owner.Partition, [ToTurnCommit(Real.Refuse(row.Id, row, Real.Delivery(m, seq), error), [])], null, $"refuse the start {m.Id}");
            Line($"deliver the start {m.Id} (creation {m.Start!.Creation?.Generation}) to {Name(row.Id)}: CreationProtocol.StartError -> {(error == "ObligationOutstanding" ? "accepted, then refused with ObligationOutstanding (the obligation is open)" : error)}; consumed, no row{(refused.Landed ? "" : "; did not land")}");
            if (!refused.Landed) Failed(owner, m, refused);
            return;
        }
        var spec = m.Start!;
        var born = new InstanceRow(row.Id, spec.Model, spec.Definition, InstanceStatus.Running, "{}", row.Revision + 1, null, Creation: spec.Creation!.Generation,
            StartIntent: Real.Fingerprint(spec, m.Kind, m.Payload));
        var result = Commit(owner.Partition, [new TurnCommit(row.Id, born, row.Revision, [m.InboxKey], [], [], [], [], Json.Write(new[] { "started" }))], null, $"the start of creation {born.Creation} at {Name(row.Id)}");
        if (result.Landed) Births.Add($"{row.Id}@{born.Creation}");
        Line($"deliver the start {m.Id} to {Name(row.Id)}: CreationProtocol.StartError accepts creation {born.Creation}; {(result.Landed ? $"the successor row lands at revision {born.Revision}" : "did not land")}");
        if (!result.Landed) Failed(owner, m, result);
    }

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
        var outcome = Real.Command(Type, owner.Worker, row, m, seq) ?? throw new InvalidOperationException("an ended row takes the actor type's own path");
        if (outcome.Row is null) { CommitOutcome(owner, row, m, outcome); return; }   // no registry and no jobs: consumed with no change (WorkflowInstance.cs 78–90)
        CommitTerminate(owner, row, m, seq, outcome, "refusing commit (J-1)");
    }

    /// <summary>
    /// A terminate's commit: the real outcome's row, consumed input and notice, with the cancels decided by the rule set
    /// and the child terminates paged. The page's ids are the built Terminate's (origin, the next revision, ordinal);
    /// the harness checks that.
    /// </summary>
    private void CommitTerminate(Owner owner, InstanceRow row, Message m, long seq, TurnOutcome outcome, string what, bool thenDelete = false)
    {
        var ob = new Obligation
        {
            Actor = row.Id, Creation = row.Creation, Parent = row.Parent, ParentCreation = row.ParentCreation,
            Children = Real.ReadRegistry(row.Children), Jobs = Real.ReadJobs(row.Jobs).Select(k => (k, JobGeneration.GetValueOrDefault((row.Id, k)))).ToList(),
            IdRevision = outcome.Row!.Revision, State = "Discharging",
        };
        var page = Compose(owner, ob, 0);
        // The built outcome cancels each job by the three-part key (WorkflowInstance.cs 198). The harness cancels the
        // generation-suffixed key; the built key must equal that key's generation-0 form for every job, in order.
        var builtCancels = outcome.Cancelled.ToList();
        if (builtCancels.Count != ob.Jobs.Count) CancelKeyMismatches++;
        else for (var i = 0; i < ob.Jobs.Count; i++)
            if (builtCancels[i] != Derive(row.Id, ob.Jobs[i].Key, row.Creation, 0)) CancelKeyMismatches++;
        var built = outcome.Sent.Where(s => s.Kind == Kinds.Terminate).ToList();
        foreach (var s in page.Sends.Where(s => s.Kind == Kinds.Terminate))
            if (!built.Any(b => b.InboxKey == s.InboxKey && b.Target == s.Target && b.TargetCreation == s.TargetCreation && b.Sender == s.Sender && b.SenderCreation == s.SenderCreation)) IdMismatches++;
        var notices = outcome.Sent.Where(s => s.Kind != Kinds.Terminate).ToList();
        var turn = ToTurnCommit(outcome, page.Consumed, [.. page.Sends, .. notices]);
        var shown = Names.ContainsKey(row.Id);
        if (shown) Line($"deliver {m.Id} (terminate from {Name(m.Sender ?? "?")}) to {Name(row.Id)}: the {what}; {row.Status} -> {outcome.Row.Status}, {ob.Children.Count} children, {ob.Jobs.Count} jobs; the built outcome cancels [{string.Join(", ", outcome.Cancelled.Select(c => c.Id))}]");
        var hook = Hook?.Invoke(row.Id, page.Number) ?? PageHook.None;
        Action onLanded = () => Applied(owner, ob, page, m);
        if (thenDelete)
        {
            // One commit terminates, sends page 1 and retires the row (proposed). The marker is the terminated row's,
            // at the commit's revision: Retire(previous) with the terminal status.
            var marker = Real.Retire(row) with { Status = outcome.Row.Status };
            var retiring = turn with { Row = marker, Delete = true, Unsubscribe = true, Journal = Json.Write(new[] { "terminated", "deleted" }) };
            var listEqual = ob.Children.SequenceEqual(Real.ReadRegistry(row.Children)) && ob.Jobs.Select(j => j.Key).SequenceEqual(Real.ReadJobs(row.Jobs));
            var shape = $"marker {marker.Status} at revision {marker.Revision}, obligation list equal to the previous registry and jobs: {listEqual}, cursor {page.To} (at most P_cascade {Rules.PCascade}: {page.To <= Rules.PCascade})";
            ThenDeleteShapes.Add(shape);
            var builtVerdict = Real.Validate(retiring, row);
            Line($"  ThenDelete: {shape}; built CreationProtocol.Validate on that retirement: {builtVerdict}");
            if (builtVerdict == "accepted" && listEqual && page.To <= Rules.PCascade && Consumed(turn, m))
            {
                // An ended row's marker is Retire(previous) as built, so the real commit carries the whole retiring commit.
                Line("  the built Validate admits it (an ended row's marker is Retire(previous)), so the real commit retires the row");
                turn = retiring;
                onLanded = () =>
                {
                    Applied(owner, ob, page, m);
                    DrainOpen.Remove(row.Id);
                    if (ob.Open) ob.MarkerRevision = marker.Revision;
                };
            }
            else if (Rules.WidenedValidate && listEqual && page.To <= Rules.PCascade && Consumed(turn, m))
            {
                // The widened Validate admits exactly this shape. The built one, inside CommitAsync, refuses it, so the
                // real commit carries the terminate (row write, counts, sends) and the harness puts the marker in its
                // place in the same step.
                Line("  the widened Validate (proposed) admits that shape; the real commit lands the terminate, and the harness writes the marker in its place");
                onLanded = () =>
                {
                    Applied(owner, ob, page, m);
                    Store.Instances[row.Id] = marker;
                    DrainOpen.Remove(row.Id);
                    if (ob.Open) ob.MarkerRevision = marker.Revision;
                };
            }
            else turn = retiring;
        }
        var result = Commit(owner.Partition, [turn], onLanded, $"{Name(row.Id)}'s {what}", hook, MarksOf(page));
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
            if (!parked.Landed) Failed(owner, m, parked);
            return;
        }
        if (result.Error != "crash before the commit") Failed(owner, m, result);
    }

    private static bool Consumed(TurnCommit turn, Message m) => turn.Consumed.Contains(m.InboxKey) || turn.Done.Contains(m.InboxKey);

    private void CommitOutcome(Owner owner, InstanceRow row, Message m, TurnOutcome outcome, Obligation? heldAtRetirement = null)
    {
        Action? onLanded = null;
        string? note = null;
        if (outcome.Delete && heldAtRetirement is { Open: true } held)
            onLanded = () =>
            {
                held.MarkerRevision = outcome.Row?.Revision ?? 0;
                Obligations[row.Id] = held;
                note = $"  the marker keeps the Held obligation as at retirement: {held}";
            };
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
        if (result.Landed && outcome.Delete) AfterDelete?.Invoke(row.Id);
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
        var result = Commit(owner.Partition, [turn], () => Applied(owner, ob, page, t), $"the refusing commit at {Name(marker.Id)}'s marker", hook, MarksOf(page));
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
        if (hook == PageHook.CrashLost) LostPages.Add((actor, page.Number, control?.InboxKey));
        var where = row.Retired ? "marker" : "row";
        var result = Commit(owner.Partition, [turn], () => Applied(owner, ob, page, control), $"page {page.Number} of {Name(actor)}'s cascade", hook, MarksOf(page));
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
        if (page.Number == 1 && page.From == 0) FirstPageJobEntries[ob.Actor] = Math.Min(page.To, ob.Jobs.Count);
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

    /// <summary>Every child terminate a page sends is kernel-composed and carries the cascade origin mark.</summary>
    private static List<InboxKey> MarksOf(Page page) => [.. page.Sends.Where(s => s.Kind == Kinds.Terminate).Select(s => s.InboxKey)];

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
            page.Sends.Add(ModeledTicket.Issue(new Message(FormattableString.Invariant($"{origin}#cascade.{ob.NextControl}"),
                new InstanceRef(ob.Actor), CascadeKind, Json.Write(new { Page = ob.NextControl }), ob.Actor,
                TargetCreation: ob.Creation, SenderCreation: ob.Creation).WithInboxScope(CascadeScope)));
            page = page with { SentControl = true };
        }
        return page;
    }

    /// <summary>
    /// R5-B1. Reviewed: every listed ticket is a counted consume. Repaired: the owner decides presence under its own
    /// fence, from the store's row at its epoch: pending is consumed (counted 1; the in-process driver cancel is not modelled), done and
    /// kept has its kept state cleared, absent is nothing.
    /// </summary>
    private void Cancel(Owner owner, Obligation ob, (string Key, long Generation) job, Page page)
    {
        var origin = Rules.ForeignDerivation && ForeignActor is { } foreign ? foreign : ob.Actor;
        var keys = Rules.GenerationBlind
            ? Store.Messages.Values.Where(r => r.Message.Kind == Kinds.Job && r.Message.Target.Id == origin && r.Message.TargetCreation == ob.Creation && JobKey(r.Message) == job.Key)
                .OrderBy(r => r.Seq).Select(r => r.Message.InboxKey).ToList()
            : [Rules.GenerationInsideKey ? DeriveInsideKey(origin, job.Key, ob.Creation, job.Generation) : Derive(origin, job.Key, ob.Creation, job.Generation)];
        foreach (var key in keys)
        {
            if (Rules.CountedCancel) { page.Consumed.Add(key); page.Notes.Add($"{key.Id}: counted consume"); continue; }
            var row = Store.Messages.GetValueOrDefault(key);
            var present = Rules.PresenceFromMemory ? !owner.Settled.Contains(key) : row is not null;
            var source = Rules.PresenceFromMemory ? "owner memory" : $"store row at epoch {owner.Epoch}";
            if (!present) page.Notes.Add($"{key.Id}: absent ({source}), nothing");
            else if (!Rules.PresenceFromMemory && row!.Done) { page.KeptCleared.Add(key); page.Notes.Add($"{key.Id}: done and kept ({KeptBy.GetValueOrDefault(key, "no KeptBy")}), kept state cleared"); }
            else { page.Consumed.Add(key); page.Notes.Add($"{key.Id}: pending ({source}), consumed (counted 1; driver cancel not modelled)"); }
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
            // The handed-over jobs must equal the registry's current-generation pairs as a set, each exactly once.
            // The reviewed text (data-lifecycle.md 102 at 0a1bb5e0c) has two checks only: every pair in the registry, and no
            // more pairs than it holds. Rules.AcceptDuplicatePair runs those, which admit a repeated pair.
            var off = jobs.Where(j => !registry.Contains(j)).ToList();
            var exact = jobs.Count == registry.Count && jobs.Distinct().Count() == jobs.Count && jobs.ToHashSet().SetEquals(registry)
                && children.Distinct().Count() == children.Count;   // and each child appears once
            var twoChecks = off.Count == 0 && jobs.Count <= registry.Count;
            if (!(Rules.AcceptDuplicatePair ? twoChecks : exact))
            {
                var text = $"the retirement list [{string.Join(", ", jobs.Select(j => $"{j.Key}@g{j.Generation}"))}] is not the registry's pairs [{string.Join(", ", registry.Select(j => $"{j.Item1}@g{j.Item2}"))}], each once";
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
            Store.Ghost.Count == 0 ? "none" : string.Join(" | ", Store.Ghost), ExpectedGhost.Count == 0 ? "none" : string.Join(" | ", ExpectedGhost));
        Report.Expect("cascade child terminates whose id differs from the built Terminate's composition", IdMismatches, 0);
        Report.Expect("built Terminate cancel keys that are not the generation-0 form of the harness's cancel keys", CancelKeyMismatches, 0);
        Line($"commits through InMemoryStore.CommitAsync: {Real.Calls.GetValueOrDefault("InMemoryStore.CommitAsync")}, terminate and cascade commits with entries (every actor) {Pages}, largest commit {LargestCommit} sends (cap {SendCap})");
    }
}
