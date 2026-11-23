using Kernel.EntityFramework;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresDelayPreview
{
    private const string Definition = "native-delay";
    private const int DelayMs = 8_000;

    internal sealed record Restart(string Reference, long Epoch, long Due, string Handle, string Cursor);

    internal static async Task<int> RunAsync(bool owned, bool inBatch, Restart? restart)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
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
        var scope = new Contract.BoundScope("preview-delay");
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 32),
            WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition,
            Wf.Sequence("sequence", Wf.Delay("wait", DelayMs), Wf.Set("finished-delay", "result", "continued"))));
        var starts = new List<(InstanceRef Actor, long Now)>();
        var applications = new List<(Message Input, long Now)>();
        var ordinary = new List<Message>();
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, "timer-" + Environment.ProcessId, 0, registry, 2, stop,
            (target, inputs, now) =>
            {
                if (inputs.Any(input => input.Start is not null)) starts.Add((target, now));
                foreach (var input in inputs.Where(m => m.Kind == NativeKinds.Timer))
                    if (input.InboxKey.Scope == -1) applications.Add((input, now)); else ordinary.Add(input);
            });
        await using var activation = await host.TryActivateAsync(stop) ?? throw new InvalidOperationException("The timer owner could not take its partition.");
        var owner = (Worker)activation;
        var epoch = owner.Fences.Single().Epoch;

        if (restart is null)
        {
            var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, "one-delay", NativeExecutionModel.Start(Definition),
                cancellationToken: stop);
            Check(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the public start must be accepted");
            var reference = receipt.Actor!.Value;
            await DriveUntil(async () => await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop) is not null);
            var image = (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop))!;
            var wait = Json.Read<NativeState>(image.State!).Waits.Values.Single(w => w.Kind == "timer");
            Check(starts.Count == 1 && starts[0].Actor.Id == image.Actor.Id, "the issuing turn must be the one start of this root");
            var issuedAt = starts[0].Now;
            var due = checked(issuedAt + DelayMs);
            Check(wait.Due == due, "the saved deadline must be the issuing turn time plus the authored Delay");
            await using var db = Open();
            var key = CreationProtocol.Origin(image.Actor.Id, image.Actor.Creation) + "#" + wait.Key;
            Check(await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Messages" m JOIN "Instances" i ON m."Target" = i."Id" AND m.xmin = i.xmin
                   WHERE m."Id" = {key} AND m."InboxScope" = {EfStore.RootSelfScope} AND m."Due" = {due} AND NOT m."Done"
                     AND i."Id" = {image.Actor.Id} AND i."Revision" = {image.Revision}) AS "Value"
                """).SingleAsync(stop), "the timer and its waiting checkpoint must share a PostgreSQL transaction");
            var journal = await api.TailAsync(scope, reference, cancellationToken: stop);
            Check(applications.Count == 0 && journal.Next is not null, "start must leave the timer pending");
            Console.WriteLine($"waiting {Environment.ProcessId} {reference.Id} {epoch} {due} {issuedAt} {wait.Key} {journal.Next!.Value.Value}");
            return 0;
        }

        var actor = new Contract.ActorRef(restart.Reference);
        Check(epoch > restart.Epoch, "the cold owner must take a newer epoch");
        var cold = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
        var saved = Json.Read<NativeState>(cold.State!).Waits.Single(p => p.Value.Kind == "timer");
        Check(saved.Value.Key == restart.Handle && saved.Value.Due == restart.Due, "cold checkpoint must retain the original deadline and handle");
        var future = await store.PollAsync(0, epoch, TimeSpan.FromMilliseconds(DelayMs * 2), 2, stop);
        Check(future.Now < restart.Due && future.Messages.Any(m => m.Message.Id == restart.Handle && m.Message.Due == restart.Due),
            "horizon read must see the pending future timer");
        var ready = await store.PollAsync(0, epoch, TimeSpan.Zero, 2, stop);
        Check(ready.Now < restart.Due && ready.Messages.All(m => m.Message.Id != restart.Handle), "ready poll must exclude the future timer");
        var app = await api.SendAsync(scope, new Contract.Target.Ref(actor), "ordinary-notice", NativeKinds.Timer, Json.Write(new TimerFired(saved.Key)),
            cancellationToken: stop);
        Check(app.Outcome == Contract.AdmissionOutcome.Accepted, "an ordinary application message can use the same kind");
        await DriveUntil(() => Task.FromResult(ordinary.Count == 1));
        var early = (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop))!;
        var earlyState = Json.Read<NativeState>(early.State!);
        Check(early.AsOf < restart.Due && applications.Count == 0 && ordinary.Single().Sender is null && ordinary.Single().Id != restart.Handle &&
            early.Status == InstanceStatus.Running && earlyState.Waits[saved.Key] == saved.Value && !earlyState.Variables.ContainsKey("result"),
            "ordinary input leaves the original wait pending before the deadline");
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
        await using (var db = Open())
            Check(!await db.Messages.AnyAsync(m => m.InboxScope == EfStore.RootSelfScope && m.Target == cold.Actor.Id, stop) &&
                (await api.TailAsync(scope, actor, cursor, stop)).Items.SequenceEqual(suffix.Items), "timer consumption and journal must remain stable");
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} epoch {epoch} due {restart.Due} applied {applications.Single().Now} checkpoint {finished.TerminalAt} timer 1");
        return 0;

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
