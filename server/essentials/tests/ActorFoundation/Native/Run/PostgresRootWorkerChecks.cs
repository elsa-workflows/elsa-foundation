using System.Text;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Microsoft.EntityFrameworkCore;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

/// <summary>Run birth and restart as separate processes against the same dedicated PostgreSQL database.</summary>
internal static class PostgresRootWorkerChecks
{
    // An ASCII value large enough to exercise the checkpoint envelope while leaving the
    // workflow's state framing and terminal header room to grow.
    private const int LargeApprovalLength = 16_410_000;

    internal static async Task<int> RunAsync(string phase, string? reference, string mode = Modes.Balanced)
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

        var birth = phase is "birth" or "budget-birth";
        var budget = phase is "budget-birth" or "budget-restart";
        if (birth)
        {
            await store.CreateAsync(1);
            await ingress.InstallAsync();
            var spec = PublicActor.Start(NativeExecutionModel.Start(DirectApprovalWorkflow.Definition, mode));
            var admission = await ingress.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), new(spec.Type, spec.TypeData, spec.Input));
            reference = admission.Reference;
        }
        if (reference is null || phase is not ("birth" or "restart" or "bounds" or "budget-birth" or "budget-restart")) throw new ArgumentException("Supply the birth or restart phase and its reference.");
        await using var owner = await PostgresNativeRootOwner.ActivateAsync(store, $"preview-{Environment.ProcessId}", tenant, reference, registry);
        Console.WriteLine($"owner {Environment.ProcessId}");
        if (birth)
        {
            var started = await Drive(1, "birth");
            var frame = Json.Read<NativeState>(started.State!).Frames.Single().Key;
            var approval = budget ? new string('x', LargeApprovalLength) : "Ada";
            if (!await Accept("approval-1", approval, frame)) throw new InvalidOperationException("First approval was declined.");
            var checkpoint = await Drive(2, "first approval");
            var state = Json.Read<NativeState>(checkpoint.State!);
            if (checkpoint.Status != InstanceStatus.Running || state.Mode != mode ||
                state.Frames[frame].State!.AsArray().Single()!.GetValue<string>() != approval) return 1;
            if (budget) await CheckBudget(checkpoint, 2);
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
            if (budget) await CheckBudget(same, 2);
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
            var approvals = Json.Read<NativeState>(ended.State!).Results["approve"]!.AsArray();
            if (ended is not { Status: InstanceStatus.Finished, TerminalRevision: 3, TerminalAt: not null } ||
                approvals.Count != 2 || approvals[1]!.GetValue<string>() != "Grace") return 1;
            var first = approvals[0]!.GetValue<string>();
            if (budget)
            {
                if (first.Length != LargeApprovalLength || first.AsSpan().IndexOfAnyExcept('x') >= 0) return 1;
                await CheckBudget(ended, 3);
            }
            else if (first != "Ada") return 1;
            Console.WriteLine(budget ? "completed the near-limit approval and Grace once" : "completed Ada, Grace once");
        }
        return 0;

        async Task CheckBudget(EfStore.RootImage image, long revision)
        {
            await using var db = new KernelDb(probeOptions);
            var physical = await db.Database.SqlQuery<StoredText>($"""
                SELECT octet_length("State") AS "StateBytes",
                    octet_length("FoundationHeader") AS "HeaderBytes",
                    octet_length("Id") + octet_length("FoundationReference") + octet_length("FoundationTenant")
                    + octet_length("Model") + octet_length("Definition")
                    + coalesce(octet_length("Parent"), 0) + coalesce(octet_length("Fault"), 0)
                    + coalesce(octet_length("Children"), 0) + coalesce(octet_length("Jobs"), 0)
                    + coalesce(octet_length("CustomHandles"), 0) AS "OtherBytes"
                FROM "Instances" WHERE "Id" = {image.Actor.Id} AND "FoundationReference" = {reference}
                    AND "FoundationTenant" = {tenant} AND "Revision" = {revision}
                """).SingleAsync();
            var journal = await probe.ReadJournalOfAsync(0, image.Actor.Id, 0);
            var bytes = physical.StateBytes + physical.HeaderBytes + physical.OtherBytes;
            var logicalStateBytes = Encoding.UTF8.GetByteCount(image.State!);
            var approvals = journal.SelectMany(entry => Json.Read<JsonElement[]>(entry.Entries))
                .Where(entry => entry.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetString())
                .Where(entry => entry?.StartsWith("approved:", StringComparison.Ordinal) is true);
            string[] expectedApprovals = revision == 2 ? ["approved:approval-1"] : ["approved:approval-1", "approved:approval-2"];
            if (physical.StateBytes <= logicalStateBytes || physical.HeaderBytes <= 0 ||
                bytes < LargeApprovalLength || bytes > 16_457_728 || !approvals.SequenceEqual(expectedApprovals) ||
                !journal.Select(entry => entry.Revision).SequenceEqual(Enumerable.Range(1, (int)revision).Select(value => (long)value)))
                throw new InvalidOperationException("The persisted checkpoint or journal missed the near-limit qualification.");
            Console.WriteLine($"revision {revision}: stored text {bytes}, state {physical.StateBytes}, framing {physical.StateBytes - logicalStateBytes}, header {physical.HeaderBytes}, journal revisions {journal.Count}");
        }

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

    private sealed record StoredText(int StateBytes, int HeaderBytes, int OtherBytes);
}
