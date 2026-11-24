using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.Native.RetentionFixture;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootRetentionChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootRetention(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = deadline.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the retention fixture uses an empty store");
        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var cursor = await admission.OpenJournalCursorAsync(stop);
        var reads = new PostgresRootReads(cold, cursor, 2);
        var blue = ContractScopes.Bind("retention-blue");
        var red = ContractScopes.Bind("retention-red");
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, blue), stop, (scope, _) => ReferenceEquals(scope, blue));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("finish", Wf.Sequence("done")))
            .Add(Wf.Workflow("wait", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));

        var expired = await Start(blue, "expired");
        var later = await Start(blue, "policy-later");
        var indefinite = await Start(blue, "keep");
        var protectedActor = await Start(blue, "protected", "wait");
        var running = await Start(blue, "running", "wait");
        var otherTenant = await Start(red, "expired");

        var actorType = new PublicActor([new PostgresRootThenDeleteChecks.SeededReceipt(new NativeExecutionModel(registry),
            reference => reference == protectedActor)]);
        var extended = false;
        const int expiryMilliseconds = 25;
        TimeSpan? Window(Contract.ActorHeader header) => header.Ref == indefinite || header.Tenant != blue.Tenant ? null
            : extended && header.Ref == later ? TimeSpan.FromHours(2)
            : header.Ref == expired ? TimeSpan.FromMilliseconds(expiryMilliseconds) : TimeSpan.Zero;
        var retention = new RetentionFixture(reads, Window, admission.AdmitRetentionDeleteAsync);
        EfStore.RootImage expiredBefore;
        EfStore.RootImage laterBefore;
        EfStore.RootImage protectedBefore;
        long originalEpoch;
        await using (var owner = await Own("before-retention"))
        {
            expiredBefore = await Drive(owner, blue, expired, InstanceStatus.Finished);
            laterBefore = await Drive(owner, blue, later, InstanceStatus.Finished);
            await Drive(owner, blue, indefinite, InstanceStatus.Finished);
            await Drive(owner, blue, running, InstanceStatus.Running);
            await Drive(owner, red, otherTenant, InstanceStatus.Finished);
            await Drive(owner, blue, protectedActor, InstanceStatus.Running);
            var protectedSelection = (await reads.GetAsync(blue, protectedActor, stop))!;
            Check((await runtime.TerminateAsync(blue, "cancel-protected", protectedSelection, cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "the protected Native workflow is selected for ordinary cancellation");
            protectedBefore = await Drive(owner, blue, protectedActor, InstanceStatus.Cancelled);
            originalEpoch = ((Worker)owner).Epoch;
        }

        var policyHistory = await cold.ReadJournalOfAsync(0, laterBefore.Actor.Id, 0, stop);
        var modelHistory = await cold.ReadJournalOfAsync(0, protectedBefore.Actor.Id, 0, stop);
        Check(policyHistory.Count > 0 && modelHistory.Count > 0,
            "both retained checkpoints have committed history before the retention scan");
        var chosen = (await reads.GetAsync(blue, expired, stop))!;
        var reissued = (await reads.GetAsync(blue, expired, stop))!;
        Check(chosen.Selection != reissued.Selection && chosen.Header.EndedAt == reissued.Header.EndedAt,
            "a fresh read seals the same terminal mark with a new token");
        Check((await admission.AdmitRetentionDeleteAsync(blue, later, chosen.Selection!, stop)).Outcome == Contract.AdmissionOutcome.Refused &&
              (await admission.AdmitRetentionDeleteAsync(red, expired, chosen.Selection!, stop)).Outcome == Contract.AdmissionOutcome.Refused,
            "retention admission binds the selection to the exact actor and tenant");
        var due = checked(expiredBefore.TerminalAt!.Value + expiryMilliseconds);
        await using (var db = Open())
        {
            long now;
            do
            {
                now = await db.Database.SqlQueryRaw<long>("""
                    SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
                    """).SingleAsync(stop);
                if (now < due) await Task.Delay(1, stop);
            } while (now < due);
            Check(now >= due && due > expiredBefore.TerminalAt, "PostgreSQL time reaches the committed root's positive retention deadline");
        }
        await InterruptAfterAdmission();
        var first = await Sweep(blue);
        Check(first is (5, 2, 1, 3), "the real retention pass replays an interrupted admission across bounded PostgreSQL read pages");
        var pending = (await admission.ReadDueRootOperatorAsync(blue.Tenant, expired.Id, stop))!;
        Check(pending.Operator is { RetentionDelete: true, QualifiedTerminal: not null } &&
              pending.Id.StartsWith("~retention-", StringComparison.Ordinal) &&
              await admission.ReadDueRootOperatorAsync(red.Tenant, otherTenant.Id, stop) is null,
            "only the trusted pass queues the private retention command");
        await using (var db = Open())
        {
            var row = await db.Messages.AsNoTracking().SingleAsync(message => message.Id == pending.InboxKey.Id, stop);
            Check(row.Start is not null && !row.Start.StartsWith("!kernel-operator/1:", StringComparison.Ordinal) &&
                  !row.Start.StartsWith("!kernel-operator/2:", StringComparison.Ordinal),
                "the stored retention envelope is distinguishable from either older operator framing");
            try
            {
                PostgresRootAdmission.DecodeOperator(row, expired.Id, true, true, true);
                throw new InvalidOperationException("The prior profile accepted a retention delete.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                Console.WriteLine("ok the prior operator profile refuses the persisted retention command");
            }
        }
        Check((await ingress.SendAsync(blue, new Contract.Target.Ref(expired), pending.Id, Contract.Kinds.Delete,
            JsonSerializer.Serialize(new Contract.OperatorCommand(chosen.Selection)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Refused, "a public Delete cannot claim the private retention operation id");
        var replay = await Sweep(blue);
        Check(replay is (5, 0, 3, >= 3) && (await admission.ReadDueRootOperatorAsync(blue.Tenant, expired.Id, stop)) == pending,
            "scanning freshly sealed pages reuses the same operations and occurrences");
        Check(await Sweep(red) is (1, 0, 0, 1), "the other tenant uses its own retention policy");

        extended = true;
        await using (var owner = await Own("retention-successor"))
        {
            Check(((Worker)owner).Epoch > originalEpoch, "a new fenced owner evaluates the current policy");
            while (await admission.ReadDueRootOperatorAsync(blue.Tenant, expired.Id, stop) is not null ||
                   await admission.ReadDueRootOperatorAsync(blue.Tenant, later.Id, stop) is not null ||
                   await admission.ReadDueRootOperatorAsync(blue.Tenant, protectedActor.Id, stop) is not null)
                await Next(owner);
        }

        Check(await reads.GetAsync(blue, expired, stop) is null &&
              (await cold.ReadJournalOfAsync(0, expiredBefore.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }],
            "the eligible expired root uses the fenced physical retirement path");
        var policyKept = (await cold.ReadFirstRootAsync(blue.Tenant, later.Id, stop))!;
        var modelKept = (await cold.ReadFirstRootAsync(blue.Tenant, protectedActor.Id, stop))!;
        Check(SameCheckpoint(laterBefore, policyKept) && SameCheckpoint(protectedBefore, modelKept) &&
              (await cold.ReadJournalOfAsync(0, laterBefore.Actor.Id, 0, stop)).SequenceEqual(policyHistory) &&
              (await cold.ReadJournalOfAsync(0, protectedBefore.Actor.Id, 0, stop)).SequenceEqual(modelHistory),
            "the extended policy and protected Native receipt independently preserve state and history");
        Check(await reads.GetAsync(blue, indefinite, stop) is not null && await reads.GetAsync(blue, running, stop) is not null &&
              await reads.GetAsync(red, otherTenant, stop) is not null,
            "indefinitely retained, running and foreign-tenant roots remain available");
        Check((await admission.AdmitRetentionDeleteAsync(blue, expired, reissued.Selection!, stop)).Outcome == Contract.AdmissionOutcome.Duplicate,
            "the original retention operation returns its admission receipt after retirement");
        return 0;

        async Task InterruptAfterAdmission()
        {
            using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var admissions = 0;
            var pass = new RetentionFixture(reads, Window, async (scope, actor, selection, token) =>
            {
                var result = await admission.AdmitRetentionDeleteAsync(scope, actor, selection, token);
                if (result.Outcome == Contract.AdmissionOutcome.Accepted)
                {
                    admissions++;
                    interrupted.Cancel();
                }
                return result;
            });

            Contract.PageToken? from = null;
            try
            {
                do
                {
                    var page = await pass.RunAsync(blue, from, interrupted.Token);
                    if (interrupted.IsCancellationRequested)
                        throw new InvalidOperationException("The cancelled page returned a continuation.");
                    from = page.Next;
                } while (from is not null);
            }
            catch (OperationCanceledException) when (interrupted.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                Check(admissions == 1, "interruption after one committed admission returns no continuation");
                return;
            }
            throw new InvalidOperationException("The interrupted retention pass returned a continuation.");
        }

        async Task<(int Examined, int Accepted, int Duplicates, int Pages)> Sweep(Contract.BoundScope scope)
        {
            var (examined, accepted, duplicates, pages) = (0, 0, 0, 0);
            Contract.PageToken? from = null;
            do
            {
                var page = await retention.RunAsync(scope, from, stop);
                (examined, accepted, duplicates, pages) = (examined + page.Examined, accepted + page.Accepted, duplicates + page.Duplicates, pages + 1);
                from = page.Next;
            } while (from is not null);
            return (examined, accepted, duplicates, pages);
        }

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string id, string definition = "finish") =>
            (await runtime.StartAsync(scope, id, NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value;

        async Task<IScopeActivation> Own(string node)
        {
            var store = Store();
            var roots = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var type = new ActorAdapter(new EfStore.RootHeaders(roots), actorType, retentionWindow: Window);
            var host = new DurableHost([new Worker(node, 0, store, type, new WorkerOptions { Partitions = 1 })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        async Task Next(IScopeActivation owner)
        {
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Contract.BoundScope scope, Contract.ActorRef target, InstanceStatus status)
        {
            while (true)
            {
                await Next(owner);
                if (await cold.ReadFirstRootAsync(scope.Tenant, target.Id, stop) is { } image && image.Status == status) return image;
            }
        }
    }

    private static bool SameCheckpoint(EfStore.RootImage before, EfStore.RootImage after) =>
        (after with { AsOf = before.AsOf, Labels = before.Labels }) == before &&
        before.Labels.Count == after.Labels.Count &&
        before.Labels.All(pair => after.Labels.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
