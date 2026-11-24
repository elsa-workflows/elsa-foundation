using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Microsoft.EntityFrameworkCore;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

/// <summary>Run birth and restart as separate processes against the same dedicated PostgreSQL database.</summary>
internal static class PostgresRootWorkerChecks
{
    internal static async Task<int> RunAsync(string phase, string? reference)
    {
        const string tenant = "preview";
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to the dedicated preview database.");
        var sql = new Counting();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(sql, sql.Transactions).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true);
        var probeOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var probe = new EfStore(() => new KernelDb(probeOptions), postgres: true);
        var ingress = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());

        if (phase == "birth")
        {
            await store.CreateAsync(1);
            await ingress.InstallAsync();
            var spec = PublicActor.Start(NativeExecutionModel.Start(DirectApprovalWorkflow.Definition));
            var admission = await ingress.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), new(spec.Type, spec.TypeData, spec.Input));
            reference = admission.Reference;
        }
        if (reference is null || phase is not ("birth" or "restart" or "bounds")) throw new ArgumentException("Supply the birth or restart phase and its reference.");
        await using var owner = await PostgresNativeRootOwner.ActivateAsync(store, $"preview-{Environment.ProcessId}", tenant, reference, registry);
        Console.WriteLine($"owner {Environment.ProcessId}");
        if (phase == "birth")
        {
            var started = await Drive(1, "birth");
            var frame = Json.Read<NativeState>(started.State!).Frames.Single().Key;
            if (!await Accept("approval-1", "Ada", frame)) throw new InvalidOperationException("First approval was declined.");
            var checkpoint = await Drive(2, "first approval");
            if (checkpoint.Status != InstanceStatus.Running ||
                Json.Read<NativeState>(checkpoint.State!).Frames[frame].State!.AsArray().Single()!.GetValue<string>() != "Ada") return 1;
            Console.WriteLine($"reference {reference}");
        }
        else
        {
            var before = await Read();
            if (before is not { Revision: 2, Status: InstanceStatus.Running }) return 1;
            var frame = Json.Read<NativeState>(before.State!).Frames.Single().Key;
            sql.Reset();
            if (await Accept("approval-1", "alternative", frame)) return 1;
            await owner.WokenAsync(null);
            await owner.RunAsync();
            Report("duplicate");
            var same = await Read();
            if (same.Revision != 2 || same.State != before.State) return 1;
            if (!await Accept("approval-2", "Grace", frame)) return 1;
            var unrelated = phase == "bounds" ? await CheckBoundaries(before) : null;
            var ended = await Drive(3, "final approval");
            if (unrelated is not null)
            {
                await using var db = Open();
                if (await db.Subscriptions.CountAsync(s => s.Instance == unrelated) != 1) return 1;
                await db.Subscriptions.Where(s => s.Instance == unrelated).ExecuteDeleteAsync();
                Console.WriteLine("terminal transition preserved an unrelated root's wait");
            }
            if (ended is not { Status: InstanceStatus.Finished, TerminalRevision: 3, TerminalAt: not null } ||
                !Json.Read<NativeState>(ended.State!).Results["approve"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(["Ada","Grace"])) return 1;
            Console.WriteLine("completed Ada, Grace once");
        }
        return 0;

        async Task<string> CheckBoundaries(EfStore.RootImage current)
        {
            var worker = (Worker)owner;
            var fence = worker.Fences.Single();
            var input = (await ingress.ReadPendingDirectAsync(tenant, reference)).Single();
            var row = (await store.LoadAsync([current.Actor.Id])).Single() with { Revision = 3, Status = InstanceStatus.Finished };
            var supported = new TurnCommit(row.Id, row, 2, [], [input.InboxKey], [], [], [], """["end"]""", Unsubscribe: true);
            var baseline = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10), false, [supported], [], []);
            foreach (var extra in new[] { supported with { Sent = [new Message("unused", new(row.Id), "extra")] },
                supported with { FoundationPublicationSequence = 17 } })
            {
                try { await store.CommitAsync(baseline with { Turns = [extra] }); }
                catch (NotSupportedException) { continue; }
                throw new InvalidOperationException("A root checkpoint silently dropped an authored effect.");
            }

            await using var db = Open();
            var other = $"0/unrelated-{Guid.NewGuid():N}";
            db.Subscriptions.AddRange(new SubscriptionEntity { Key = "checks", Instance = row.Id, Wait = "open", Partition = fence.Partition },
                new SubscriptionEntity { Key = "checks", Instance = other, Wait = "open", Partition = fence.Partition });
            await db.SaveChangesAsync();
            if ((await store.CommitAsync(baseline)).Landed || (await Read()).Revision != 2 ||
                !(await ingress.ReadPendingDirectAsync(tenant, reference)).Select(m => m.InboxKey).SequenceEqual([input.InboxKey]) ||
                (await probe.ReadJournalOfAsync(fence.Partition, row.Id, 0)).Count != 2 ||
                await db.Partitions.Where(p => p.Id == fence.Partition).Select(p => p.Position).SingleAsync() != fence.Position ||
                await db.Subscriptions.CountAsync(s => s.Instance == row.Id || s.Instance == other) != 2)
                throw new InvalidOperationException("A terminal refusal changed the checkpoint, input, journal, position or waits.");
            await db.Subscriptions.Where(s => s.Instance == row.Id && s.Key == "checks" && s.Wait == "open").ExecuteDeleteAsync();
            Console.WriteLine("extra sends and publication effects refused; terminal guard rolled back all writes");
            return other;
        }

        async Task<bool> Accept(string id, string name, string frame) =>
            await ingress.AdmitDirectAsync(tenant, reference, id, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", name)))
                ?? throw new InvalidOperationException("Target unavailable.");

        async Task<EfStore.RootImage> Read() => await probe.ReadFirstRootAsync(tenant, reference)
            ?? throw new InvalidOperationException("Root checkpoint was not stored.");

        async Task<EfStore.RootImage> Drive(long expected, string label)
        {
            sql.Reset();
            await owner.WokenAsync(null);
            for (var pass = 0; pass < 8; pass++)
            {
                await owner.RunAsync();
                if (await probe.ReadFirstRootAsync(tenant, reference) is { } observed && observed.Revision == expected)
                {
                    Report(label);
                    return observed;
                }
                await Task.Yield();
            }
            throw new InvalidOperationException($"The worker did not commit revision {expected}.");
        }

        void Report(string phaseName) => Console.WriteLine($"{phaseName}: {sql.Commands} commands, {sql.Transactions.Ended} transaction completions, {sql.Commands + sql.Transactions.Ended} round trips");
    }
}
