using System.Globalization;
using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.Native.RetentionFixture;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootRetentionChecks
{
    private const string Expiring = "expire";
    private const string PolicyChange = "policy-change";
    private const string Keep = "keep";
    private const string Wait = "wait";

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
        var reads = await cold.OpenPreviewRootReadsAsync(pageSize: 2, cancellationToken: stop);
        var blue = ContractScopes.Bind("retention-blue");
        var red = ContractScopes.Bind("retention-red");
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, blue), stop, (scope, _) => ReferenceEquals(scope, blue));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow(Expiring, Wf.Sequence("done")))
            .Add(Wf.Workflow(PolicyChange, Wf.Sequence("done")))
            .Add(Wf.Workflow(Keep, Wf.Sequence("done")))
            .Add(Wf.Workflow(Wait, Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));

        var expired = await Start(blue, "expired", Expiring);
        var later = await Start(blue, "policy-later", PolicyChange);
        var indefinite = await Start(blue, "keep", Keep);
        var protectedActor = await Start(blue, "protected", Wait);
        var running = await Start(blue, "running", Wait);
        var otherTenant = await Start(red, "expired", Expiring);

        var native = new NativeExecutionModel(registry);
        var actorType = new PublicActor([new PostgresRootThenDeleteChecks.SeededReceipt(native, reference => reference == protectedActor)]);
        var extended = false;
        const int expiryMilliseconds = 25;
        TimeSpan? Window(Contract.ActorHeader header) => header.Tenant != blue.Tenant ? null
            : header.Labels.GetValueOrDefault("definition") switch
            {
                Expiring => TimeSpan.FromMilliseconds(expiryMilliseconds),
                PolicyChange when extended => TimeSpan.FromHours(2),
                PolicyChange or Wait => TimeSpan.Zero,
                _ => null
            };
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
        await CheckProjectedCandidates(due);
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

        async Task CheckProjectedCandidates(long endedBefore)
        {
            try
            {
                // The real checkpoint reader first pins the journal's schema. Keep that prerequisite
                // until the missing checkpoint columns have caused the real read to fail.
                await SetOwnerReadPaths(hideCheckpoint: true, hideJournal: false, stop);
                await RequireOwnerReadFailure(() => cold.ReadFirstRootAsync(blue.Tenant, expired.Id, stop),
                    PostgresErrorCodes.UndefinedColumn, "the real owner checkpoint lookup fails while state, header and terminal columns are unavailable");
                await SetOwnerReadPaths(hideCheckpoint: true, hideJournal: true, stop);
                await RequireOwnerReadFailure(() => cold.ReadJournalOfAsync(0, expiredBefore.Actor.Id, 0, stop).AsTask(),
                    PostgresErrorCodes.UndefinedTable, "the real owner journal lookup fails while the journal relation is unavailable");

                var query = new Contract.ActorQuery(Status: Contract.ActorStatus.Finished, EndedBefore: endedBefore,
                    Labels: new Dictionary<string, string> { ["definition"] = Expiring });
                var list = await runtime.ListAsync(blue, query, cancellationToken: stop);
                var point = await reads.GetAsync(blue, expired, stop);
                var terminalAt = expiredBefore.TerminalAt!.Value;
                var terminalRevision = expiredBefore.TerminalRevision!.Value;
                Check(list is { Items: [var view], Next: null } && point is not null && view.Header.Ref == expired &&
                    view.Header.Type == PublicActor.ActorType && view.Header.Labels["definition"] == Expiring &&
                    view.Header.EndedAt == terminalAt && point.Header.EndedAt == terminalAt && view.AsOf == list.AsOf &&
                    PostgresRootSelection.MatchesTerminal(cursor, view.Selection, blue.Tenant, expired.Id, terminalRevision, terminalAt) &&
                    PostgresRootSelection.MatchesTerminal(cursor, point.Selection, blue.Tenant, expired.Id, terminalRevision, terminalAt),
                    "public list and point reads return the definition, end time and exact terminal selection from the projection");
                Check((await runtime.ListAsync(blue, query with
                    { Labels = new Dictionary<string, string> { ["definition"] = "missing" } }, cancellationToken: stop)).Items.Count == 0 &&
                    await reads.GetAsync(blue, otherTenant, stop) is null,
                    "a different definition and a foreign exact reference are excluded by the public projection");

                Check((await runtime.ListAsync(blue, query with { EndedBefore = expiredBefore.TerminalAt }, cancellationToken: stop)).Items.Count == 0,
                    "the ended-before filter excludes the exact end timestamp");

                var runningList = await runtime.ListAsync(blue, new(Status: Contract.ActorStatus.Running,
                    Labels: new Dictionary<string, string> { ["definition"] = Wait }), cancellationToken: stop);
                Check(runningList.Items is [var runningView] && runningView.Header.Ref == running && runningView.Header.EndedAt is null,
                    "the running definition filter excludes the cancelled actor with the same definition");
                var current = runningList.Items.Single().Header;
                using var metadata = JsonDocument.Parse(current.TypeData);
                var workflow = metadata.RootElement;
                Check(workflow.GetProperty("Model").GetString() == NativeExecutionModel.ModelKey &&
                    workflow.GetProperty("Definition").GetString() == Wait && !workflow.GetProperty("Ephemeral").GetBoolean() &&
                    workflow.GetProperty("Version").GetInt32() == native.StateVersion && !current.Labels.ContainsKey("version"),
                    "the projected running header carries the model, definition, state version and persistence mode used by migration selection");

                var candidates = new HashSet<Contract.ActorRef>();
                var planning = new RetentionFixture(reads, Window, (scope, actor, selection, _) =>
                {
                    Check(scope == blue && selection.Length > 0 && candidates.Add(actor),
                        "the retention plan proposes one opaque selection per candidate");
                    return new(new Contract.Admission(Contract.AdmissionOutcome.Accepted, Actor: actor));
                });
                Contract.PageToken? from = null;
                do { from = (await planning.RunAsync(blue, from, stop)).Next; }
                while (from is not null);
                Check(candidates.SetEquals([expired, later, protectedActor]),
                    "the retention pass derives its exact candidates from projected definitions and end times while the owner lookups are unavailable");
            }
            finally
            {
                using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await SetOwnerReadPaths(hideCheckpoint: false, hideJournal: false, restore.Token);
            }
        }

        async Task SetOwnerReadPaths(bool hideCheckpoint, bool hideJournal, CancellationToken token)
        {
            // Keep Instances and its triggers visible to the projection's catalog readiness query.
            string[] columns = ["State", "FoundationStateWire", "StateVersion", "FoundationHeader", "FoundationTenant",
                "FoundationReference", "FoundationTerminalAt", "FoundationTerminalRevision", "Status", "Revision", "Retired"];
            await using var db = Open();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var journalHidden = await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('"FixtureJournalRows"') IS NOT NULL AS "Value"
                """).SingleAsync(token);
            var checkpointHidden = await db.Database.SqlQueryRaw<bool>("""
                SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute
                    WHERE attrelid = to_regclass('"Instances"') AND NOT attisdropped AND attname = 'FoundationStateWire') AS "Value"
                """).SingleAsync(token);
            if (checkpointHidden != hideCheckpoint)
            {
                // All identifiers are fixed fixture names. Commit these renames together for readers on other connections.
                await using var ddl = db.Database.GetDbConnection().CreateCommand();
                ddl.Transaction = transaction.GetDbTransaction();
                var renames = columns.Select(column => hideCheckpoint
                    ? $"ALTER TABLE \"Instances\" RENAME COLUMN \"{column}\" TO \"Fixture{column}\""
                    : $"ALTER TABLE \"Instances\" RENAME COLUMN \"Fixture{column}\" TO \"{column}\"");
                ddl.CommandText = string.Join(";\n", renames);
                await ddl.ExecuteNonQueryAsync(token);
            }
            if (journalHidden != hideJournal)
                await db.Database.ExecuteSqlRawAsync(hideJournal
                    ? "ALTER TABLE \"Journal\" RENAME TO \"FixtureJournalRows\""
                    : "ALTER TABLE \"FixtureJournalRows\" RENAME TO \"Journal\"", token);

            var expectedColumns = hideCheckpoint ? 0 : columns.Length;
            var valid = await db.Database.SqlQuery<bool>($"""
                SELECT (to_regclass('"Journal"') IS NULL) = {hideJournal}
                   AND (to_regclass('"FixtureJournalRows"') IS NOT NULL) = {hideJournal}
                   AND (SELECT count(*) FROM pg_catalog.pg_attribute
                        WHERE attrelid = to_regclass('"Instances"') AND NOT attisdropped
                          AND attname::text = ANY({columns})) = {expectedColumns} AS "Value"
                """).SingleAsync(token);
            await transaction.CommitAsync(token);
            Check(valid, hideCheckpoint
                ? hideJournal ? "the canonical owner state wire, header, terminal and journal lookup names are absent"
                    : "the canonical owner state wire, header and terminal lookup names are absent"
                : "the canonical owner checkpoint and journal lookup names are restored");
        }

        static async Task RequireOwnerReadFailure(Func<Task> read, string sqlState, string description)
        {
            try { await read(); }
            catch (PostgresException error) when (error.SqlState == sqlState)
            {
                Console.WriteLine($"ok {description}");
                return;
            }
            throw new InvalidOperationException(description);
        }

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

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string id, string definition) =>
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
