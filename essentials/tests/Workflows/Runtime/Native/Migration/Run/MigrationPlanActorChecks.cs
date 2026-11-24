using System.Security.Cryptography;
using System.Text;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.WorkflowActor;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workflows.Runtime.Native.Migration;

internal static class MigrationPlanActorChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long initial = 1_701_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("acme");
        var key = RandomNumberGenerator.GetBytes(32);
        var authority = new MigrationPlanAuthority(key);
        var foundation = new InMemoryActorFixture(store, "real-plan", 1,
            (_, kind) => kind is Contract.Kinds.Migrate or Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var ingress = foundation.Ingress;
        var reads = foundation.Reads;
        var runtime = new PublicRuntime(ingress, reads);
        var tooWide = new string('é', 600);
        var registry = new WorkflowRegistry().Add(Definition("source", "old")).Add(Definition("target", "new"))
            .Add(Definition(tooWide, "too wide"));
        var plans = new MigrationPlanModel(authority, (executionModel, from, to) =>
            (executionModel is null || executionModel == NativeExecutionModel.ModelKey) &&
            registry.TryGet(from, out _) && registry.TryGet(to, out _));
        var actorType = new PublicWorkflow([new NativeExecutionModel(registry), plans, new ReadOnlyJournalModel()]);
        var deadline = now + 600_000;
        var invalid = false;
        try { _ = plans.Start(new("missing", "target", deadline)); }
        catch (ArgumentException) { invalid = true; }
        check(invalid, "an unavailable source definition is refused before plan admission");
        var wide = false;
        try { _ = plans.Start(new("source", tooWide, deadline)); }
        catch (ArgumentException) { wide = true; }
        check(wide, "plan creation enforces the apply path's UTF-8 definition budget");

        var plan = await StartPlan("main-plan", deadline);
        var declaredFull = PublicWorkflow.Start(plans.Start(new("source", "target", deadline)));
        var actualMinimal = await StartRawPlan("minimal-history", declaredFull with { History = Contract.History.Minimal });
        var actualEphemeral = await StartRawPlan("ephemeral-plan", declaredFull with { Ephemeral = true });
        await StartWorkflow("first-workflow");
        await StartWorkflow("second-workflow");
        await using var owner = await Open("first-plan-owner");
        await Pump(owner);
        check((await reads.GetAsync(scope, actualMinimal))?.Header.Status == Contract.ActorStatus.Faulted &&
            (await reads.GetAsync(scope, actualEphemeral))?.Header.Status is null or Contract.ActorStatus.Faulted &&
            (await reads.TailAsync(scope, actualMinimal)).Items.All(entry => MigrationPlanJournal.Read(entry.Entry) is not MigrationPlanJournal.Created) &&
            (await reads.TailAsync(scope, actualEphemeral)).Items.All(entry => MigrationPlanJournal.Read(entry.Entry) is not MigrationPlanJournal.Created),
            "the creator turn rejects contradictory foundation history and ephemeral settings");
        var query = new Contract.ActorQuery(PublicWorkflow.ActorType, Contract.ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = "source" });
        var firstPage = await runtime.ListAsync(scope, query);
        var first = firstPage.Items.Single().Header.Ref;
        var fixture = (await runtime.StartAsync(scope, "other-journal",
            new(ReadOnlyJournalModel.ModelKey, "other", Json.Write(first)))).Actor!.Value;
        await Pump(owner);
        check((await reads.TailAsync(scope, fixture)).Items.Count > 0 &&
            await NewReader(fixture).ReadPendingAsync(scope, fixture, default) is null,
            "a different actor cannot supply a runnable batch even when it has a readable Full journal");
        var cursor = firstPage.Next ?? throw new InvalidOperationException("The source selection should have another page.");
        var firstControl = new MigrationPlanControl(1, [first], After: cursor);
        var checkpoint = NewReader(plan);
        var target = new Contract.Target.Ref(plan);
        await ingress.SendAsync(scope, target, "first-page", MigrationPlanControl.Select, Json.Write(firstControl));
        var copied = authority.Protect(scope.Tenant, first, MigrationPlanControl.Select, "wrong-plan", firstControl);
        await ingress.SendAsync(scope, target, "wrong-plan", MigrationPlanControl.Select, copied);
        var tooMany = Enumerable.Range(0, MigrationPlanModel.BatchLimit + 1)
            .Select(index => new Contract.ActorRef($"selected-{index}")).ToArray();
        await checkpoint.SendAsync(MigrationPlanControl.Select, "oversize", firstControl with { Actors = tooMany });
        await checkpoint.SendAsync(MigrationPlanControl.Select, "empty-cursor", firstControl with { After = new("") });
        await Pump(owner);
        check(await checkpoint.ReadPendingAsync(scope, plan, default) is null,
            "raw, cross-plan, oversized and empty-cursor controls leave the batch slot empty");

        await checkpoint.SendAsync(MigrationPlanControl.Select, "first-page", firstControl);
        await Pump(owner);
        var secondPage = await runtime.ListAsync(scope, query, cursor);
        var second = secondPage.Items.Single().Header.Ref;
        var finalControl = new MigrationPlanControl(2, [second], Before: cursor, AllSelected: true);
        await checkpoint.SendAsync(MigrationPlanControl.Select, "early-page", finalControl);
        var earlyCompletion = await checkpoint.TryCompleteAsync();
        await Pump(owner);
        var pending = await checkpoint.ReadPendingAsync(scope, plan, default);
        check(!earlyCompletion && pending is { Number: 1, Actors: [var selected] } && selected == first &&
            (await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Running,
            "the real plan keeps one pending batch and rejects a premature page or completion");

        now += 20_000;
        await using var successor = await Open("second-plan-owner");
        var nextReader = NewReader(plan, new MigrationPlanAuthority(key));
        var dispatcher = new MigrationPlanDispatch(ingress, nextReader);
        var dispatched = await dispatcher.DispatchAsync(scope, plan);
        var replay = await dispatcher.DispatchAsync(scope, plan);
        check(dispatched is { Accepted: 1 } && replay is { Duplicate: 1 } &&
            await nextReader.ReadPendingAsync(scope, plan, default) is { Number: 1 },
            "after takeover a fresh runner reconstructs the same batch and deduplicates both admissions before the acknowledgment commits");
        await Pump(successor);
        check(await nextReader.ReadPendingAsync(scope, plan, default) is null, "committed acknowledgment clears the exact pending batch");

        await nextReader.SendAsync(MigrationPlanControl.Select, "incorrect-cursor", finalControl with { Before = null });
        await Pump(successor);
        check(await nextReader.ReadPendingAsync(scope, plan, default) is null, "a stale page cursor cannot advance the plan");
        await nextReader.SendAsync(MigrationPlanControl.Select, "next-page", finalControl);
        await Pump(successor);
        var last = await dispatcher.DispatchAsync(scope, plan);
        await Pump(successor);
        check(last is { Accepted: 1 } && await nextReader.ReadPendingAsync(scope, plan, default) is null,
            "the follow-on page dispatches its own exact actor and clears after acknowledgment");
        await ingress.SendAsync(scope, target, "unsigned-answer", MigrationPlanResult.Kind,
            Json.Write(new MigrationPlanResult(MigrationPlanOutcome.Refused)));
        await Pump(successor);
        var outcomes = new MigrationPlanOutcomes(reads, scope, plan);
        await Refresh(outcomes);
        var opened = outcomes.Read();
        check(opened is { Closed: false, AllAnswered: true } && opened.Results.Count == 2 &&
            opened.Results[first] == MigrationPlanOutcome.Migrated && opened.Results[second] == MigrationPlanOutcome.Migrated,
            "the journal projects both actual Native outcomes and ignores an outside result with no actor sender");
        var requested = await nextReader.TryCompleteAsync();
        await Pump(successor);
        await Refresh(outcomes);
        var saved = foundation.ReadCheckpoint(scope.Tenant, plan.Id)!.State;
        check(requested && outcomes.Read().Closed && (await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Finished &&
            !saved.Contains(first.Id, StringComparison.Ordinal) && !saved.Contains(second.Id, StringComparison.Ordinal) &&
            (await reads.InboxAsync(scope, plan)).Items.All(item => item.Kind != MigrationPlanControl.Deadline),
            "completion retains bounded plan state and recalls the still-future deadline");

        await ingress.SendAsync(scope, new Contract.Target.Event("event:approval"), "approve", "publish", "yes");
        await Pump(successor);
        check(FinishedUnderTarget(first) && FinishedUnderTarget(second), "both migrated Native waits resume under the target definition");

        var unanswered = await StartPlan("unanswered-plan", deadline);
        var departing = await StartWorkflow("departing-selection");
        await Pump(successor);
        var unansweredReader = NewReader(unanswered);
        await unansweredReader.SendAsync(MigrationPlanControl.Select, "departure-batch", new(1, [departing], AllSelected: true));
        await Pump(successor);
        var departingTarget = new Contract.Target.Ref(departing);
        await ingress.SendAsync(scope, departingTarget, "end-departing", Contract.Kinds.Terminate);
        await Pump(successor);
        var deletion = (await reads.GetAsync(scope, departing))!.Selection!;
        await ingress.SendAsync(scope, departingTarget, "delete-departing", Contract.Kinds.Delete,
            Json.Write(new Contract.OperatorCommand(deletion)));
        await Pump(successor);
        var missing = await new MigrationPlanDispatch(ingress, unansweredReader).DispatchAsync(scope, unanswered);
        await Pump(successor);
        check(missing is { Unavailable: 1 } && await unansweredReader.ReadPendingAsync(scope, unanswered, default) is null &&
            !await unansweredReader.TryCompleteAsync() &&
            (await reads.GetAsync(scope, unanswered))?.Header.Status == Contract.ActorStatus.Running,
            "an acknowledged final page with a deleted, unanswered life cannot complete automatically");

        var empty = await StartPlan("empty-plan", deadline);
        await Pump(successor);
        var emptyReader = NewReader(empty);
        var beforeSelection = await emptyReader.TryCompleteAsync();
        await emptyReader.SendAsync(MigrationPlanControl.Select, "empty-last-page", new(1, [], AllSelected: true));
        await Pump(successor);
        var afterSelection = await emptyReader.TryCompleteAsync();
        await Pump(successor);
        check(!beforeSelection && afterSelection && (await reads.GetAsync(scope, empty))?.Header.Status == Contract.ActorStatus.Finished,
            "an explicit empty final page completes without a dispatch acknowledgment");

        var forced = await StartPlan("force-close-plan", deadline);
        var late = await StartWorkflow("force-close-selection");
        await Pump(successor);
        var forcedReader = NewReader(forced);
        await forcedReader.SendAsync(MigrationPlanControl.Select, "force-close-batch", new(1, [late], AllSelected: true));
        await Pump(successor);
        await forcedReader.SendAsync(MigrationPlanControl.Close, "close", new(1));
        await Pump(successor);
        var forcedOutcomes = new MigrationPlanOutcomes(reads, scope, forced);
        await Refresh(forcedOutcomes);
        check(forcedOutcomes.Read() is { Closed: true } closedEarly && closedEarly.NoAnswer.SequenceEqual([late]) &&
            (await reads.InboxAsync(scope, forced)).Items.All(item => item.Kind != MigrationPlanControl.Deadline),
            "an operator close recalls the future deadline and reports its selected life as unanswered");
        await ingress.SendAsync(scope, new Contract.Target.Ref(late), "force-close-late", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("source", "target", forced)));
        await Pump(successor);
        await Refresh(forcedOutcomes);
        check(forcedOutcomes.Read().Results[late] == MigrationPlanOutcome.Migrated,
            "the original actor can answer after the operator closes the plan");

        var cancelled = await StartPlan("cancelled-plan", deadline);
        var secondSelection = await StartWorkflow("cancelled-selection");
        await Pump(successor);
        var cancelledReader = NewReader(cancelled);
        await cancelledReader.SendAsync(MigrationPlanControl.Select, "cancelled-batch",
            new(1, [secondSelection], AllSelected: true));
        await Pump(successor);
        check(await cancelledReader.ReadPendingAsync(scope, cancelled, default) is { Number: 1 },
            "the cancellation case begins with a cached committed batch");
        var openPlan = (await reads.GetAsync(scope, cancelled))!;
        var openState = foundation.ReadCheckpoint(scope.Tenant, cancelled.Id)!.State;
        var growth = actorType.TerminalGrowth(openPlan.Header, openState);
        await ingress.SendAsync(scope, new Contract.Target.Ref(cancelled), "terminate-plan", Contract.Kinds.Terminate);
        await Pump(successor);
        var cancelledState = foundation.ReadCheckpoint(scope.Tenant, cancelled.Id)!.State;
        check(Encoding.UTF8.GetByteCount(cancelledState) - Encoding.UTF8.GetByteCount(openState) == 2 && growth >= 2,
            "the reserved terminal growth covers clearing a one-digit pending batch");
        var cancelledOutcomes = new MigrationPlanOutcomes(reads, scope, cancelled);
        await Refresh(cancelledOutcomes);
        check((await reads.GetAsync(scope, cancelled))?.Header.Status == Contract.ActorStatus.Cancelled &&
            await new MigrationPlanDispatch(ingress, cancelledReader).DispatchAsync(scope, cancelled) is null &&
            await NewReader(cancelled).ReadPendingAsync(scope, cancelled, default) is null &&
            cancelledOutcomes.Read() is { Closed: true } closed && closed.NoAnswer.SequenceEqual([secondSelection]) &&
            (await reads.GetAsync(scope, secondSelection))?.Header.Labels.GetValueOrDefault("definition") == "source",
            "cancellation stops both cached and freshly rebuilt dispatch and closes the outcome view");

        var expiry = now + 200;
        var expiring = await StartPlan("expiring-plan", expiry);
        var waiting = await StartWorkflow("late-workflow");
        await Pump(successor);
        var beforeExpiry = (await reads.GetAsync(scope, expiring))!;
        var waitingState = foundation.ReadCheckpoint(scope.Tenant, expiring.Id)!.State;
        var timer = (await reads.InboxAsync(scope, expiring)).Items.Single();
        check(actorType.RestoreWaits(beforeExpiry.Header, waitingState).Timers is
            [{ Id: var timerId, Kind: MigrationPlanControl.Deadline, Payload: null, Due: var due }] &&
            timerId == timer.Id && due == expiry && timer.Due == expiry,
            "the recovery recipe names the exact issued deadline and its original due time");
        var expiryReader = NewReader(expiring);
        await expiryReader.SendAsync(MigrationPlanControl.Select, "late-selection", new(1, [waiting], AllSelected: true));
        await Pump(successor);
        check(await expiryReader.ReadPendingAsync(scope, expiring, default) is { Number: 1 },
            "the pending selection can be released before the deadline");
        await ingress.SendAsync(scope, new Contract.Target.Ref(waiting), "delayed-migration", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("source", "target", expiring)), new(DueAt: expiry + 100));
        var lateOutcomes = new MigrationPlanOutcomes(reads, scope, expiring);
        now = expiry;
        check((await reads.GetAsync(scope, expiring))?.Header.Status == Contract.ActorStatus.Running &&
            await expiryReader.ReadPendingAsync(scope, expiring, default) is null &&
            await NewReader(expiring).ReadPendingAsync(scope, expiring, default) is null,
            "the deadline stops both cached and cold dispatch while the owner has not run");
        await Pump(successor);
        await Refresh(lateOutcomes);
        check(lateOutcomes.Read() is { Closed: true } expired && expired.NoAnswer.SequenceEqual([waiting]) &&
            await expiryReader.ReadPendingAsync(scope, expiring, default) is null,
            "the plan's own scheduled deadline closes the undispatched page and preserves its no-answer view");
        now = expiry + 100;
        await Pump(successor);
        await Refresh(lateOutcomes);
        check(lateOutcomes.Read() is { Closed: true } answered && answered.NoAnswer.Count() == 0 &&
            answered.Results.GetValueOrDefault(waiting) == MigrationPlanOutcome.Migrated,
            "a late Native result updates the same closed plan from the exact selected life");
        await CheckParkAsync(check);

        async Task<Contract.ActorRef> StartPlan(string id, long expires) =>
            (await runtime.StartAsync(scope, id, plans.Start(new("source", "target", expires)))).Actor!.Value;
        async Task<Contract.ActorRef> StartRawPlan(string id, Contract.StartSpec spec) =>
            (await ingress.SendAsync(scope, new Contract.Target.Start(spec), id, Contract.Kinds.Start)).Actor!.Value;
        async Task<Contract.ActorRef> StartWorkflow(string id) =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start("source"))).Actor!.Value;
        async Task<IScopeActivation> Open(string node) => (await new DurableHost(
            [new Worker(node, 0, store, foundation.Bind(actorType), new WorkerOptions { Monotonic = () => now - initial })])
            .ActivateAsync(0))!;
        JournalMigrationDispatchState NewReader(Contract.ActorRef reference, MigrationPlanAuthority? authenticator = null) =>
            new(ingress, reads, authenticator ?? authority, scope, reference);
        bool FinishedUnderTarget(Contract.ActorRef reference) =>
            foundation.ReadCheckpoint(scope.Tenant, reference.Id) is { Status: InstanceStatus.Finished } row &&
            Json.Read<NativeState>(row.State).Variables["output"]?.GetValue<string>() == "new";
    }

    private static async Task CheckParkAsync(Action<bool, string> check)
    {
        const long initial = 1_701_900_000_000;
        const long parkWindow = 1_000;
        var now = initial;
        var store = InMemoryActorFixture.CreateWithParkRetention(() => now, TimeSpan.FromMilliseconds(parkWindow));
        var scope = ContractScopes.Bind("plan-park");
        var authority = new MigrationPlanAuthority(RandomNumberGenerator.GetBytes(32));
        var foundation = new InMemoryActorFixture(store, "plan-park", 8,
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Terminate or Contract.Kinds.Migrate or Contract.Kinds.Discard);
        var ingress = foundation.Ingress;
        var reads = foundation.Reads;
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Definition("source", "old")).Add(Definition("target", "new"));
        var plans = new MigrationPlanModel(authority, (executionModel, from, to) =>
            executionModel is null or NativeExecutionModel.ModelKey && registry.TryGet(from, out _) && registry.TryGet(to, out _));
        var wrapper = new ParkPlanActor(new PublicWorkflow([new NativeExecutionModel(registry), plans]));
        var worker = new Worker("plan-park", 0, store, foundation.Bind(wrapper), new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var timerPlan = await StartPlan("retry-deadline", initial + 200);
        await Pump(owner);
        wrapper.FailDeadline = true;
        now += 200;
        await Pump(owner);
        var parkedTimer = await reads.GetAsync(scope, timerPlan);
        var timer = foundation.ResolveTargetId(scope, timerPlan);
        check(parkedTimer is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
            foundation.HasConsumedDelivery(timer, MigrationPlanControl.Deadline),
            "a failed deadline is retained by the actual Park path");
        await Resolve(timerPlan, "retry", Contract.Kinds.Recover, Contract.RecoverMode.RetryAll);
        await Pump(owner);
        var timerHistory = new MigrationPlanOutcomes(reads, scope, timerPlan);
        await Refresh(timerHistory);
        check((await reads.GetAsync(scope, timerPlan))?.Header.Status == Contract.ActorStatus.Finished &&
            timerHistory.Read().Closed && wrapper.DeadlineTurns == 2,
            "RetryAll restores the original deadline and the real plan closes on that delivery");

        var plan = await StartPlan("terminate-open-recovery", initial + 60_000);
        var selected = (await runtime.StartAsync(scope, "native", NativeExecutionModel.Start("source"))).Actor!.Value;
        await Pump(owner);
        var dispatch = new JournalMigrationDispatchState(ingress, reads, authority, scope, plan);
        await dispatch.SendAsync(MigrationPlanControl.Select, "parked-selection", new(1, [selected], AllSelected: true));
        await Pump(owner);
        check(await dispatch.ReadPendingAsync(scope, plan, default) is { Number: 1 },
            "dispatch has a cached committed batch before the plan parks");
        await ingress.SendAsync(scope, new Contract.Target.Ref(plan), "park", "fixture-park");
        await Pump(owner);
        for (var i = 0; i < 2; i++)
            await ingress.SendAsync(scope, new Contract.Target.Ref(plan), $"kept-{i}", "arrival",
                new string('x', InMemoryActorFixture.RecoveryCreditLimit + 1));
        await Pump(owner);
        await Resolve(plan, "resume-open", Contract.Kinds.Recover, Contract.RecoverMode.Resume);
        await Step(plan);
        check(await reads.GetAsync(scope, plan) is { Park: not null, Header.Status: Contract.ActorStatus.Running } &&
            await dispatch.ReadPendingAsync(scope, plan, default) is null,
            "an open recovery with a Running header keeps the cached batch unavailable");
        await Resolve(plan, "terminate-recovery", Contract.Kinds.Terminate);
        var physicalPlan = foundation.ResolveTargetId(scope, plan);
        for (var pass = 0; pass < 5 && !foundation.HasDiscardDrain(physicalPlan); pass++) await Step(plan);
        check(await reads.GetAsync(scope, plan) is { Park.OpenResolution: "terminating" } &&
            await dispatch.ReadPendingAsync(scope, plan, default) is null,
            "a parked termination keeps dispatch closed throughout the drain");
        await Pump(owner);
        var outcomes = new MigrationPlanOutcomes(reads, scope, plan);
        await Refresh(outcomes);
        check((await reads.GetAsync(scope, plan))?.Header.Status == Contract.ActorStatus.Cancelled &&
            outcomes.Read() is { Closed: true } closed && closed.NoAnswer.SequenceEqual([selected]),
            "the completed parked termination closes the projection and lists the unanswered life");
        await ingress.SendAsync(scope, new Contract.Target.Ref(selected), "late-migrate", Contract.Kinds.Migrate,
            Json.Write(new MigrationRequest("source", "target", plan)));
        await Pump(owner);
        await Refresh(outcomes);
        check(outcomes.Read() is { Closed: true } cleared && cleared.NoAnswer.SequenceEqual([selected]) &&
            (await reads.GetAsync(scope, selected))?.Header.Labels.GetValueOrDefault("definition") == "target",
            "Park termination keeps the unanswered classification when an already-admitted migration later applies");

        foreach (var expire in new[] { false, true })
        {
            var action = expire ? "expiry" : "discard";
            var subject = await StartPlan(action, initial + 60_000);
            await Pump(owner);
            var reader = new JournalMigrationDispatchState(ingress, reads, authority, scope, subject);
            await reader.SendAsync(MigrationPlanControl.Select, "final-page", new(1, [selected], AllSelected: true));
            await Pump(owner);
            await ingress.SendAsync(scope, new Contract.Target.Ref(subject), action, "fixture-park");
            await Pump(owner);
            var projection = new MigrationPlanOutcomes(reads, scope, subject);
            await Refresh(projection);
            var parked = projection.Read();
            if (expire) now += parkWindow;
            else await Resolve(subject, action, Contract.Kinds.Discard);
            await Pump(owner);
            await Refresh(projection);
            check(parked is { Closed: false } && parked.Pending.SequenceEqual([selected]) &&
                projection.Read() is { Closed: true } final && final.NoAnswer.SequenceEqual([selected]) &&
                await reads.GetAsync(scope, subject) is { Header.Status: Contract.ActorStatus.Faulted, Park: null },
                $"Park {action} closes the projected plan while an unresolved Park keeps its selections pending");
        }

        async Task<Contract.ActorRef> StartPlan(string operation, long deadline) =>
            (await runtime.StartAsync(scope, operation, plans.Start(new("source", "target", deadline)))).Actor!.Value;

        async Task Resolve(Contract.ActorRef planRef, string operation, string kind, Contract.RecoverMode mode = Contract.RecoverMode.Resume)
        {
            var selection = (await reads.GetAsync(scope, planRef))?.Selection ??
                throw new InvalidOperationException("The parked plan has no selection.");
            var reply = await ingress.SendAsync(scope, new Contract.Target.Ref(planRef), operation, kind,
                Json.Write(new Contract.OperatorCommand(selection, mode)));
            if (reply.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The plan operator request {operation} was refused.");
        }

        async Task Step(Contract.ActorRef planRef)
        {
            var actor = foundation.ResolveTargetId(scope, planRef);
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(actor);
            await worker.FlushAsync();
        }
    }

    private sealed class ParkPlanActor(PublicWorkflow actor) : Contract.IActorType
    {
        public string Type => actor.Type;
        internal bool FailDeadline { get; set; }
        internal int DeadlineTurns { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "fixture-park"))
                throw new InvalidOperationException("The plan Park fixture interrupted its turn.");
            if (turn.Inbox.Any(message => message.Kind == MigrationPlanControl.Deadline))
            {
                DeadlineTurns++;
                if (FailDeadline)
                {
                    FailDeadline = false;
                    throw new InvalidOperationException("The plan Park fixture interrupted its deadline.");
                }
            }
            return actor.TurnAsync(turn, cancellationToken);
        }

        public bool Withholds(Contract.ActorHeader header) => actor.Withholds(header);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => actor.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => actor.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => actor.ProtectsDeletion(header, state, now);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
            actor.ExecuteJobAsync(job, cancellationToken);
    }

    private sealed class ReadOnlyJournalModel : Models.IExecutionModel
    {
        internal const string ModelKey = "test-journal";
        public string Key => ModelKey;
        public Type StateType => typeof(Contract.ActorRef);
        public int StateVersion => 1;
        public object CreateState(Models.WorkflowStart start) => Json.Read<Contract.ActorRef>(start.Input!);
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state) => new([], [], []);
        public long TerminalGrowth(Models.InstanceInfo instance, object state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
        {
            turn.Record(new MigrationPlanJournal.Created("source", "target", long.MaxValue));
            turn.Record(new MigrationPlanJournal.Selected([(Contract.ActorRef)turn.State], Number: 1));
            return ValueTask.CompletedTask;
        }
    }

    private static Workflow Definition(string name, string output) => Wf.Workflow(name,
        Wf.Sequence("sequence", Wf.Event("wait", "approval"), Wf.Set("tail", "output", output)), ("output", null));

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 30; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private static async Task Refresh(MigrationPlanOutcomes projection)
    {
        while (await projection.ProjectNextAsync()) { }
    }
}
