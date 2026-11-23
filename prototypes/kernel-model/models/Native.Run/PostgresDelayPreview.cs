using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresDelayPreview
{
    private const string Definition = "native-delay";
    private const int DelayMs = 8_000;

    internal sealed record Restart(string Reference, long Epoch, long Due, string Handle, string Cursor);

    internal static async Task<int> RunAsync(bool owned, bool inBatch, string mode, Restart? restart, bool recall = false)
    {
        if (restart is { Epoch: <= 0 })
            throw new ArgumentOutOfRangeException(nameof(restart), "Restart requires a positive prior epoch.");
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTimers(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        if (restart is null)
        {
            await store.CreateAsync(1);
            await admission.InstallAsync(stop);
        }
        await store.EnableRootSelfSendsAsync(stop);
        var scope = FixedTenantScope.Bind("preview-delay");
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), 32),
            WorkflowActor.WorkflowActor.ActorType);
        var delayed = Wf.Sequence(recall ? "slow" : "sequence", Wf.Delay("wait", DelayMs), Wf.Set("finished-delay", "result", "continued"));
        var body = recall ? Wf.Sequence("sequence", Wf.Parallel("race", 1, delayed, Wf.Node<PostgresFlowchartPreview.Approval>("win")),
            Wf.Node<PostgresFlowchartPreview.Approval>("finish")) : delayed;
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, body));
        using var trace = new System.Diagnostics.Activity("postgres.delay.preview").Start();
        var applications = new List<(Message Input, long Now)>();
        var ordinary = new List<(Message Input, long Now)>();
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, "timer-" + Environment.ProcessId, 0, registry, 2, stop,
            (_, inputs, now) =>
            {
                foreach (var input in inputs.Where(m => m.Kind == NativeKinds.Timer))
                    if (input.InboxKey.Scope == -1) applications.Add((input, now)); else ordinary.Add((input, now));
            });
        await using var activation = await host.TryActivateAsync(stop) ?? throw new InvalidOperationException("The timer owner could not take its partition.");
        var owner = (Worker)activation;
        var epoch = owner.Fences.Single().Epoch;

        if (restart is null)
        {
            var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, "one-delay", NativeExecutionModel.Start(Definition, mode),
                cancellationToken: stop);
            Check(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the public start must be accepted");
            var reference = receipt.Actor!.Value;
            await DriveUntil(async () => await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop) is { } current &&
                Json.Read<NativeState>(current.State!).Waits.Values.Any(w => w.Kind == "timer"));
            var image = (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop))!;
            var wait = Json.Read<NativeState>(image.State!).Waits.Values.Single(w => w.Kind == "timer");
            var issuance = trace.Events.Single(e => e.Name == "native.timer.issued");
            Check(Equals(issuance.Tags.Single(t => t.Key == "actor").Value, reference.Id), "the issuing turn must schedule the timer on this root");
            var issuedAt = (long)issuance.Tags.Single(t => t.Key == "issuedAt").Value!;
            var due = checked(issuedAt + DelayMs);
            Check(wait.Due == due, "the saved deadline must be the issuing turn time plus the authored Delay");
            await using var db = Open();
            var key = CreationProtocol.Origin(image.Actor.Id, image.Actor.Creation) + "#" + wait.Key;
            Check(await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Messages" m JOIN "Instances" i ON m."Target" = i."Id" AND m.xmin = i.xmin
                   WHERE m."Id" = {key} AND m."InboxScope" = {EfStore.RootSelfScope} AND m."Due" = {due} AND NOT m."Done"
                     AND i."Id" = {image.Actor.Id} AND i."Revision" = {image.Revision}) AS "Value"
                """).SingleAsync(stop), "the timer and its waiting checkpoint must share a PostgreSQL transaction");
            if (recall)
            {
                var pending = await SendNoticeAsync(reference, due);
                await ApproveAsync(reference, image, "win");
                await DriveUntil(async () => (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop))?.Revision > image.Revision);
                var won = (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop))!;
                var waiting = Json.Read<NativeState>(won.State!);
                Check(won.AsOf < due && won.Status == InstanceStatus.Running && waiting.Waits.Count == 0 &&
                    waiting.Frames.Values.Any(f => f.Node == "finish") && !waiting.Variables.ContainsKey("result"),
                    "approval must win the race while the original Delay is pending");
                Check((await admission.ReadPendingDirectAsync(scope.Tenant, reference.Id, stop)).Single() == pending &&
                    !await db.Messages.AnyAsync(m => m.Target == image.Actor.Id && m.InboxScope == EfStore.RootSelfScope, stop),
                    "the scoped recall removes its private timer and retains the exact ordinary message");
                Check(await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Instances" i JOIN "Journal" j ON i."Id" = j."Instance" AND j."Revision" = i."Revision"
                        JOIN "Messages" m ON m."Target" = i."Id" AND m."Done" AND m."InboxScope" = {InboxKey.FoundationOccurrenceScope}
                        WHERE i."Id" = {image.Actor.Id} AND i."Revision" = {won.Revision} AND m.xmin = i.xmin AND j.xmin = i.xmin) AS "Value"
                    """).SingleAsync(stop), "the ordinary approval, winning checkpoint and journal must share a transaction");
            }
            var journal = await api.TailAsync(scope, reference, cancellationToken: stop);
            if (recall) Check(journal.Items.Count(j => j.Entry.StartsWith("\"x:", StringComparison.Ordinal) &&
                j.Entry.EndsWith(":wait\"", StringComparison.Ordinal)) == 1, "the journal must record the Delay frame cancelled once");
            Check(applications.Count == 0 && journal.Next is not null, "start must leave a journal bookmark without firing the timer");
            Console.WriteLine($"waiting {Environment.ProcessId} {reference.Id} {epoch} {due} {issuedAt} {wait.Key} {journal.Next!.Value.Value}");
            return 0;
        }

        var actor = new Contract.ActorRef(restart.Reference);
        Check(epoch > restart.Epoch, "the cold owner must take a newer epoch");
        var cold = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
        var coldState = Json.Read<NativeState>(cold.State!);
        Check(coldState.Mode == mode && cold.Status == InstanceStatus.Running, "cold owner must resume the waiting actor in its original persistence mode");
        if (recall)
        {
            var pending = (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop)).Single();
            Check(pending.Id == "surviving-notice" && pending.Due == restart.Due &&
                coldState.Waits.Count == 0, "cold restart must preserve the ordinary input and cancelled checkpoint");
            var bookmark = new Contract.PageToken(restart.Cursor);
            await DriveUntil(() => Task.FromResult(ordinary.Count > 0));
            Check(ordinary is [var notice] && notice.Input == pending && notice.Now >= restart.Due && applications.Count == 0, "the original ordinary message must run once after the timer deadline");
            await ApproveAsync(actor, (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!, "finish");
            await DriveUntil(async () => (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))?.Status == InstanceStatus.Finished);
            var final = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
            var tail = await api.TailAsync(scope, actor, bookmark, stop);
            await Pass();
            Check(ordinary.Count == 1 && applications.Count == 0 && final.AsOf >= restart.Due && !Json.Read<NativeState>(final.State!).Variables.ContainsKey("result") &&
                tail.Items.Any(j => j.Entry == "\"end\"") && !tail.Items.Any(j => j.Entry.Contains("finished-delay", StringComparison.Ordinal)) &&
                (await api.TailAsync(scope, actor, bookmark, stop)).Items.SequenceEqual(tail.Items), "the recalled timer must never resume its frame");
            Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} epoch {epoch} due {restart.Due} ordinary {ordinary.Count} timer {applications.Count}");
            return 0;
        }
        var saved = coldState.Waits.Single(p => p.Value.Kind == "timer");
        Check(saved.Value.Key == restart.Handle && saved.Value.Due == restart.Due, "cold checkpoint must retain the original deadline and handle");
        var future = await store.PollAsync(0, epoch, TimeSpan.FromMilliseconds(DelayMs * 2), 2, stop);
        Check(future.Messages.Any(m => m.Message.Id == restart.Handle && m.Message.Due == restart.Due),
            "horizon read must return the pending timer with its original deadline");
        var ready = await store.PollAsync(0, epoch, TimeSpan.Zero, 2, stop);
        Check(ready.Messages.Any(m => m.Message.Id == restart.Handle) == (ready.Now >= restart.Due),
            "the ready poll must expose the timer precisely when the database deadline is due");
        var observedBeforeDue = false;
        if (ready.Now < restart.Due)
        {
            var app = await api.SendAsync(scope, new Contract.Target.Ref(actor), "ordinary-notice", NativeKinds.Timer, Json.Write(new TimerFired(saved.Key)),
                cancellationToken: stop);
            Check(app.Outcome == Contract.AdmissionOutcome.Accepted && app.Actor == actor, "an ordinary application message can use the same kind");
            await DriveUntil(() => Task.FromResult(ordinary.Count == 1 || applications.Count != 0));
            var early = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
            if (early.AsOf < restart.Due)
            {
                var earlyState = Json.Read<NativeState>(early.State!);
                Check(applications.Count == 0 && ordinary.Single().Input.Sender is null && ordinary.Single().Input.Id != restart.Handle &&
                    early.Status == InstanceStatus.Running && earlyState.Waits[saved.Key] == saved.Value && !earlyState.Variables.ContainsKey("result"),
                    "ordinary input leaves the original wait pending before the deadline");
                observedBeforeDue = true;
            }
        }
        await DriveUntil(async () => (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))?.Status == InstanceStatus.Finished);
        var finished = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
        var result = Json.Read<NativeState>(finished.State!);
        var cursor = new Contract.PageToken(restart.Cursor);
        var suffix = await api.TailAsync(scope, actor, cursor, stop);
        Check(applications is [var fired] && fired.Input.Id == saved.Value.Key && fired.Input.Due == restart.Due &&
            fired.Input.Sender == cold.Actor.Id && fired.Input.SenderCreation == cold.Actor.Creation && fired.Now >= restart.Due &&
            finished.TerminalAt >= restart.Due && result.Waits.Count == 0 && result.Variables["result"]!.GetValue<string>() == "continued",
            "only the issued timer must resume its saved frame once at or after the deadline");
        Check(suffix.Items.Count(j => j.Entry.StartsWith("\"c:", StringComparison.Ordinal) && j.Entry.EndsWith(":finished-delay\"", StringComparison.Ordinal)) == 1,
            "the journal must show the post-delay activity once");
        await Pass();
        var afterPass = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
        await using (var db = Open())
            Check(suffix.Next is not null && (await api.TailAsync(scope, actor, suffix.Next, stop)).Items.Count == 0 &&
                afterPass.Revision == finished.Revision && afterPass.State == finished.State && !await db.Messages.AnyAsync(m => m.InboxScope == EfStore.RootSelfScope && m.Target == cold.Actor.Id, stop) &&
                (await api.TailAsync(scope, actor, cursor, stop)).Items.SequenceEqual(suffix.Items), "timer consumption and journal must remain stable");
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} epoch {epoch} due {restart.Due} applied {applications.Single().Now} checkpoint {finished.TerminalAt} timer 1 early-check {observedBeforeDue} already-due {ready.Now >= restart.Due}");
        return 0;

        async Task<Message> SendNoticeAsync(Contract.ActorRef target, long due)
        {
            var accepted = await api.SendAsync(scope, new Contract.Target.Ref(target), "surviving-notice", NativeKinds.Timer,
                Json.Write(new TimerFired("notice")), new(DueAt: due), stop);
            Check(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the ordinary notice must be accepted");
            return (await admission.ReadPendingDirectAsync(scope.Tenant, target.Id, stop)).Single();
        }

        async Task ApproveAsync(Contract.ActorRef target, EfStore.RootImage image, string node)
        {
            var frame = Json.Read<NativeState>(image.State!).Frames.Values.Single(f => f.Node == node);
            var signal = new CompositeSignal(frame.Id, "approve", node);
            var accepted = await api.SendAsync(scope, new Contract.Target.Ref(target), node, CompositeSignal.Kind, Json.Write(signal), cancellationToken: stop);
            Check(accepted.Outcome == Contract.AdmissionOutcome.Accepted, "the public approval must be accepted");
        }

        async Task Pass()
        {
            await activation.WokenAsync(null, stop);
            await activation.RunAsync(stop);
            Check(owner.Owns, "the scoped owner must keep its fence");
        }

        async Task DriveUntil(Func<Task<bool>> condition)
        {
            var nextHeartbeat = Environment.TickCount64;
            while (!await condition())
            {
                if (Environment.TickCount64 >= nextHeartbeat)
                {
                    await host.HeartbeatAsync(stop);
                    nextHeartbeat = Environment.TickCount64 + 1000;
                }
                await Pass();
                await Task.Delay(30, stop);
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
