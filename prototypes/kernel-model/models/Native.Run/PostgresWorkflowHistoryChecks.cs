using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using Actor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresWorkflowHistoryChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch, string phase, Contract.PageToken? saved = null,
        string? admittedReference = null)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var counter = new Counting();
        var measured = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection)
            .AddInterceptors(counter, counter.Transactions).Options;
        KernelDb Open() => new(options);
        EfStore Store(Func<KernelDb> open) => new(open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var store = Store(Open);
        var writer = Store(() => new(measured));
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var scope = FixedTenantScope.Bind(phase == "tenant" ? PostgresMultiRootClient.Secondary : "history-blue");
        var query = new WorkflowRunQuery(DirectApprovalWorkflow.Definition, Contract.ActorStatus.Running);
        await using var db = Open();
        if (phase == "birth")
        {
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, ct), "the history fixture requires an empty database");
            await store.CreateAsync(1);
            await admission.InstallAsync(ct);
        }
        var cursor = await admission.OpenJournalCursorAsync(ct);
        var api = new PostgresPreviewFoundation(admission, new(store, cursor, 2), Actor.ActorType);
        var runs = new PostgresWorkflowRunReads(store, cursor, Actor.ActorType, 2);

        if (phase == "tenant")
        {
            var ownPage = await runs.ListAsync(scope, query, cancellationToken: ct);
            Check(ownPage is { Items: [var own], Next: null } && admittedReference is not null &&
                own.Ref.Id == admittedReference && own.Definition == query.Definition,
                "another tenant sees its own exact admitted reference and no continuation");
            await Refused(() => runs.ListAsync(scope, query, saved, ct).AsTask(), "from");
            return 0;
        }
        if (phase == "restart")
        {
            var first = await runs.ListAsync(scope, query, cancellationToken: ct);
            var next = await runs.ListAsync(scope, query, saved, ct);
            var last = await runs.ListAsync(scope, query, next.Next, ct);
            var ordered = first.Items.Concat(next.Items).Concat(last.Items).ToArray();
            var times = ordered.Select(row => row.UpdatedAt).ToArray();
            Check(first is { Items.Count: 2, Next: not null } && next is { Items.Count: 2, Next: not null } &&
                last is { Items.Count: 1, Next: null } && ordered.Select(row => row.Ref).Distinct().Count() == 5 &&
                times[0] == times[1] && times[1] == times[2] && times[2] > times[3] && times[3] == times[4] &&
                ordered.SequenceEqual(ordered.OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Ref.Id, StringComparer.Ordinal)),
                "a new process resumes a tied boundary and reaches older rows with checkpoint and journal tables unavailable");
            await Refused(() => runs.ListAsync(scope, query with { To = DateTimeOffset.UtcNow }, saved, ct).AsTask(), "from");
            await Refused(() => runs.ListAsync(scope, query with { Definition = null }, saved, ct).AsTask(), "from");
            await Refused(() => runs.ListAsync(scope, query with { Status = null }, saved, ct).AsTask(), "from");
            try { await api.TailAsync(scope, first.Items[0].Ref, saved, ct); throw new InvalidOperationException("Accepted a history bookmark on the journal."); }
            catch (ArgumentException error) when (error.ParamName == "from") { }
            Check(true, "history bookmarks bind filters and read kind");
            return 0;
        }

        await RequireFormatRefusal(() => runs.ListAsync(scope, query, cancellationToken: ct).AsTask());
        var legacy = await Start("legacy", TimeSpan.Zero);
        var baseline = await Start("baseline");
        var neighbor = new Contract.ActorRef(await PostgresMultiRootClient.CallSecondaryAsync(ct, "start"));
        var immediate = Reference.WriteLines(1);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build()).Add(immediate);
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, "history", 0, registry, 16, ct);
        await using var active = await host.TryActivateAsync(ct) ?? throw new InvalidOperationException("Expected the PostgreSQL owner.");
        await Drive(legacy, 1);
        await Drive(baseline, 1);
        await Drive(neighbor, 1, PostgresMultiRootClient.Secondary);
        var without = await CountCheckpoint(baseline);
        await CheckInstallSchema();
        await store.InstallRootWorkflowHistoryAsync(ct);
        await store.InstallRootWorkflowHistoryAsync(ct);
        Check((await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(scope, new Contract.ActorQuery(), cancellationToken: ct)).Items.Count == 2,
            "installation leaves prior roots visible on the actor list and adds no invented history times");
        var with = await CountCheckpoint(legacy);
        Check(with == without, $"one added projection upsert leaves client counts unchanged ({with.Commands} commands, {with.Begun} transaction begins, {with.Ended} completions)");
        var legacyRow = (await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Single();
        Check(legacyRow.Ref == legacy, "a prior root enters history at its first observed checkpoint");

        var completedAtBirth = await Start("completed-at-birth", definition: immediate.Id);
        var birthImage = await Drive(completedAtBirth, 1);
        var birthQuery = new WorkflowRunQuery(immediate.Id, Contract.ActorStatus.Finished);
        var birthPage = await runs.ListAsync(scope, birthQuery, cancellationToken: ct);
        Check(birthImage is { Status: InstanceStatus.Finished, TerminalRevision: 1, TerminalAt: > 0 } &&
            birthPage is { Items: [var birth], Next: null } && birth.Ref == completedAtBirth &&
            birth.UpdatedAt.ToUnixTimeMilliseconds() == birthImage.TerminalAt &&
            await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM "Instances" i JOIN "Journal" j
                        ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = 1 AND j.xmin = i.xmin
                    JOIN "FoundationActorList" l ON l."Tenant" = i."FoundationTenant" AND l."Reference" = i."FoundationReference"
                        AND l."EndedAt" = i."FoundationTerminalAt" AND l.xmin = i.xmin
                    JOIN "FoundationWorkflowHistory" h USING ("Tenant", "Reference")
                    WHERE l."Tenant" = {scope.Tenant} AND l."Reference" = {completedAtBirth.Id}
                        AND h."UpdatedAt" = i."FoundationTerminalAt" AND h."Revision" = 1 AND h.xmin = i.xmin
                ) AS "Value"
                """).SingleAsync(ct),
            "a Native run finishing at birth writes its terminal clock, journal and both projections in one PostgreSQL transaction");

        var successor = await Start("legacy", TimeSpan.Zero);
        Check(successor != legacy, "a repeated start after its receipt expires gets a distinct exact reference");
        var newer = new[] { successor, await Start("two"), await Start("three"), await Start("four") };
        var finished = await Start("finished");
        foreach (var actor in newer.Append(finished)) await Drive(actor, 1);
        var original = await Image(newer[0]);
        var firstJournal = await api.TailAsync(scope, newer[0], cancellationToken: ct);
        var min = await Clock();
        while (await Clock() <= min) await Task.Delay(1, ct);
        await Approve(newer[0], original, "first", "Ada");
        var changed = await Drive(newer[0], 2);
        var physical = await ReadPhysical(newer[0]);
        Check(physical.Revision == changed.Revision && physical.UpdatedAt > min && physical.UpdatedAt <= await Clock() &&
            physical.SameTransaction && (await api.TailAsync(scope, newer[0], firstJournal.Next, ct)).Items.Count == 0 &&
            Json.Read<NativeState>(changed.State!).Frames.Single().Value.State!.AsArray().Single()!.GetValue<string>() == "Ada",
            "a journal-silent Native update changes its real checkpoint and projection in the same PostgreSQL transaction");

        await Approve(finished, await Image(finished), "first", "Ada");
        await Drive(finished, 2);
        await Approve(finished, await Image(finished), "second", "Grace");
        var terminal = await Drive(finished, 3);
        var terminalView = (await runs.ListAsync(scope, query with { Status = Contract.ActorStatus.Finished }, cancellationToken: ct)).Items.Single();
        Check(terminalView.Ref == finished && terminalView.UpdatedAt.ToUnixTimeMilliseconds() == terminal.TerminalAt &&
            (await ReadPhysical(finished)).SameTransaction, "the finished projection reuses the qualified terminal clock");
        await CheckRollback(newer[0], physical);
        await CheckFormats(newer[0], physical);
        await CheckTrigger();
        await CheckActorListDependency();
        await CheckSourceReplacement();
        await CheckRetirementGuard(finished, terminal);

        // Seed two groups from observed times so the first cursor lands inside a tie and must also reach older rows.
        var newest = newer.Take(3).Select(actor => actor.Id).ToArray();
        var runningReferences = newer.Append(legacy).Select(actor => actor.Id).ToArray();
        var older = legacyRow.UpdatedAt.ToUnixTimeMilliseconds();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FoundationWorkflowHistory" SET "UpdatedAt" = CASE WHEN "Reference" = ANY({newest}) THEN {physical.UpdatedAt} ELSE {older} END
            WHERE "Tenant" = {scope.Tenant} AND "Reference" = ANY({runningReferences})
            """, ct);
        var tie = DateTimeOffset.FromUnixTimeMilliseconds(physical.UpdatedAt);
        var exact = query with { From = tie.ToOffset(TimeSpan.FromHours(2)), To = tie };
        var firstPage = await runs.ListAsync(scope, exact, cancellationToken: ct);
        Check(firstPage is { Items.Count: 2, Next: not null } && firstPage.Items.All(item => item.UpdatedAt == tie) &&
            (await runs.ListAsync(scope, query with { From = tie.AddTicks(1) }, cancellationToken: ct)).Items.Count == 0 &&
            (await runs.ListAsync(scope, query with { To = tie.AddTicks(-1) }, cancellationToken: ct)).Items is [var olderA, var olderB] &&
            olderA.UpdatedAt == legacyRow.UpdatedAt && olderB.UpdatedAt == legacyRow.UpdatedAt,
            "inclusive date bounds match the exact clock and exclude ticks immediately outside it");
        await Refused(() => runs.ListAsync(scope, query with { From = tie, To = tie.AddTicks(-1) }, cancellationToken: ct).AsTask(), "query");
        var savedPage = await runs.ListAsync(scope, query, cancellationToken: ct);
        // The second tenant starts a tracked root and receives its exact reference for the cold read.
        var other = new Contract.ActorRef(await PostgresMultiRootClient.CallSecondaryAsync(ct, "start-receipt", "chronology", "Accepted"));
        await Drive(other, 1, PostgresMultiRootClient.Secondary);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" RENAME TO "FixtureOwnerRows";
            ALTER TABLE "Journal" RENAME TO "FixtureJournalRows";
            """, ct);
        Console.WriteLine($"projection-bookmark {savedPage.Next!.Value.Value}");
        Console.WriteLine($"tenant-reference {other.Id}");
        return 0;

        async Task<Contract.ActorRef> Start(string id, TimeSpan? keepFor = null, string? definition = null)
        {
            var start = NativeExecutionModel.Start(definition ?? DirectApprovalWorkflow.Definition) with { History = Contract.History.Minimal };
            return (await api.SendAsync(scope, new Contract.Target.Start(Actor.Start(start)), id, Contract.Kinds.Start,
                options: new(KeepFor: keepFor), cancellationToken: ct)).Actor
                ?? throw new InvalidOperationException("Expected an admitted workflow.");
        }

        Task<EfStore.RootImage?> Read(Contract.ActorRef actor, string? tenant = null) =>
            store.ReadFirstRootAsync(tenant ?? scope.Tenant, actor.Id, ct);
        async Task<EfStore.RootImage> Image(Contract.ActorRef actor) => (await Read(actor))!;
        async Task<EfStore.RootImage> Drive(Contract.ActorRef actor, int revision, string? tenant = null)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await Read(actor, tenant) is { } found && found.Revision == revision) return found;
                await active.WokenAsync(null, ct);
                await active.RunAsync(ct);
            }
            throw new InvalidOperationException($"Missing root revision {revision}.");
        }
        async Task Approve(Contract.ActorRef actor, EfStore.RootImage image, string id, string person)
        {
            var frame = Json.Read<NativeState>(image.State!).Frames.Single().Key;
            await PostgresMultiRootClient.Send(api, scope, actor, id, Json.Write(new CompositeSignal(frame, "approve", person)),
                Contract.AdmissionOutcome.Accepted, ct);
        }
        async Task<(int Commands, int Begun, int Ended)> CountCheckpoint(Contract.ActorRef actor)
        {
            var image = await Image(actor);
            Check((await api.SendAsync(scope, new Contract.Target.Ref(actor), "empty", "probe", cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "the instrumented silent checkpoint has a real qualified input");
            var input = (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, ct)).Single();
            var fence = ((Worker)active).Fences.Single();
            counter.Reset();
            var committed = await writer.CommitRootTurnAsync(scope.Tenant, actor.Id, image, [input], fence, Defaults.Lease,
                image.Status, image.State, image.StateVersion, image.TypeData, image.Labels, null, ct);
            Check(committed.Landed, "the instrumented silent checkpoint committed");
            return (counter.Commands, counter.Transactions.Begun, counter.Transactions.Ended);
        }
        Task<PhysicalRun> ReadPhysical(Contract.ActorRef actor) => db.Database.SqlQuery<PhysicalRun>($"""
            SELECT h."Revision", h."UpdatedAt", h.xmin = i.xmin AS "SameTransaction"
            FROM "FoundationWorkflowHistory" h JOIN "Instances" i ON h."Reference" = i."FoundationReference"
                AND h."Tenant" = i."FoundationTenant"
            WHERE h."Tenant" = {scope.Tenant} AND h."Reference" = {actor.Id}
            """).SingleAsync(ct);
        Task<long> Clock() => db.Database.SqlQueryRaw<long>("""
            SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
            """).SingleAsync(ct);

        async Task CheckRollback(Contract.ActorRef actor, PhysicalRun prior)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = "Revision" + 1, "FoundationWriteRevision" = "Revision" + 1
                WHERE "FoundationTenant" = {scope.Tenant} AND "FoundationReference" = {actor.Id}
                """, ct);
            var staged = await ReadPhysical(actor);
            var publicPage = await runs.ListAsync(scope, query, cancellationToken: ct);
            Check(staged.Revision == prior.Revision + 1 && staged.SameTransaction &&
                publicPage.Items.Any(row => row.Ref == actor && row.UpdatedAt.ToUnixTimeMilliseconds() == prior.UpdatedAt),
                "another connection sees only the committed time while both revision writes are staged");
            await transaction.RollbackAsync(ct);
            Check(await ReadPhysical(actor) == prior, "rollback removes the staged revision and timestamp together");
        }
        async Task CheckFormats(Contract.ActorRef actor, PhysicalRun prior)
        {
            await db.Database.ExecuteSqlRawAsync("""UPDATE "FoundationWorkflowHistoryFormat" SET "Version" = 2 """, ct);
            await RequireFormatRefusal(() => runs.ListAsync(scope, query, cancellationToken: ct).AsTask());
            await RequireFormatRefusal(() => store.InstallRootWorkflowHistoryAsync(ct));
            await RefuseStorage(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Revision" = "Revision" + 1, "FoundationWriteRevision" = "Revision" + 1
                WHERE "FoundationReference" = {actor.Id}
                """, ct), "55000");
            await db.Database.ExecuteSqlRawAsync("""UPDATE "FoundationWorkflowHistoryFormat" SET "Version" = 1 """, ct);
            await db.Database.ExecuteSqlRawAsync("""UPDATE "KernelStoreVersions" SET "StoreSchema" = "StoreSchema" + 1 """, ct);
            await RequireFormatRefusal(() => runs.ListAsync(scope, query, cancellationToken: ct).AsTask());
            await db.Database.ExecuteSqlRawAsync("""UPDATE "KernelStoreVersions" SET "StoreSchema" = "StoreSchema" - 1 """, ct);
            Check(await ReadPhysical(actor) == prior, "an incompatible projection format refuses reads, setup and writes");
        }
        async Task CheckInstallSchema()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA fixture_history_setup", ct);
            var schema = await db.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync(ct);
            using var identifiers = new NpgsqlCommandBuilder();
            var shadowConnection = new NpgsqlConnectionStringBuilder(connection)
            {
                SearchPath = "fixture_history_setup," + identifiers.QuoteIdentifier(schema)
            }.ConnectionString;
            var shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(shadowConnection).Options;
            var shadow = Store(() => new KernelDb(shadowOptions));
            await RequireFormatRefusal(() => shadow.InstallPreviewRootSchemaAsync(ct));
            await RequireFormatRefusal(() => shadow.InstallRootWorkflowHistoryAsync(ct));
            Check(await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('fixture_history_setup."FoundationRootIssuer"') IS NULL
                    AND to_regclass('fixture_history_setup."FoundationActorList"') IS NULL
                    AND to_regclass('fixture_history_setup."FoundationWorkflowHistoryFormat"') IS NULL AS "Value"
                """).SingleAsync(ct), "setup refuses a different creation schema without creating preview relations");
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA fixture_history_setup", ct);
        }
        async Task CheckTrigger()
        {
            var trigger = await db.Database.SqlQueryRaw<string>("""
                SELECT pg_get_triggerdef(oid) AS "Value" FROM pg_catalog.pg_trigger
                WHERE tgrelid = to_regclass('"Instances"') AND tgname = 'kernel_project_root_workflow_history_v1'
                """).SingleAsync(ct);
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_workflow_history_v1 """, ct);
            await RequireSourceRefusal();
            await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER kernel_project_root_workflow_history_v1 ON "Instances" """, ct);
            await RequireSourceRefusal();
            await db.Database.ExecuteSqlRawAsync(trigger, ct);
            Check((await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Count == 2,
                "reads and installation refuse a disabled or absent trigger and reopen when it is restored");
        }
        async Task CheckActorListDependency()
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_actor_list """, ct);
            await RequireSourceRefusal();
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE REPLICA TRIGGER kernel_project_root_actor_list """, ct);
            await RequireSourceRefusal();
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_actor_list """, ct);
            Check((await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Count == 2,
                "the history route requires its actor-list trigger in an origin-enabled mode");
        }
        async Task CheckSourceReplacement()
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Instances" RENAME TO "FixtureArchivedOwner";
                CREATE TABLE "Instances" (LIKE "FixtureArchivedOwner" INCLUDING DEFAULTS INCLUDING CONSTRAINTS);
                """, ct);
            await RequireSourceRefusal();
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE "Instances" """, ct);
            Check((await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Count == 2,
                "the history route refuses a canonical replacement and accepts a renamed-only source");
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FixtureArchivedOwner" DISABLE TRIGGER kernel_project_root_actor_list;
                CREATE SCHEMA fixture_other_history;
                CREATE TABLE fixture_other_history."Instances" (id integer);
                DO $fixture$
                DECLARE trigger_name text;
                BEGIN
                    FOREACH trigger_name IN ARRAY ARRAY['kernel_require_root_checkpoint_writer',
                        'kernel_project_root_actor_list', 'kernel_project_root_workflow_history_v1']
                    LOOP
                        EXECUTE format('CREATE FUNCTION fixture_other_history.%I() RETURNS trigger LANGUAGE plpgsql AS %L',
                            trigger_name, 'BEGIN RETURN NEW; END');
                        EXECUTE format('CREATE TRIGGER %1$I AFTER UPDATE ON fixture_other_history."Instances"
                            FOR EACH ROW EXECUTE FUNCTION fixture_other_history.%1$I()', trigger_name);
                    END LOOP;
                END
                $fixture$;
                """, ct);
            await RequireFormatRefusal(() => runs.ListAsync(scope, query, cancellationToken: ct).AsTask());
            await db.Database.ExecuteSqlRawAsync("""
                DROP SCHEMA fixture_other_history CASCADE;
                ALTER TABLE "FixtureArchivedOwner" ENABLE TRIGGER kernel_project_root_actor_list;
                """, ct);
            Check((await runs.ListAsync(scope, query, cancellationToken: ct)).Items.Count == 2,
                "a complete trigger chain in another schema cannot certify the renamed history source");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FixtureArchivedOwner" RENAME TO "Instances" """, ct);
        }
        async Task RequireSourceRefusal()
        {
            await RequireFormatRefusal(() => runs.ListAsync(scope, query, cancellationToken: ct).AsTask());
            await RequireFormatRefusal(() => store.InstallRootWorkflowHistoryAsync(ct));
        }
        async Task CheckRetirementGuard(Contract.ActorRef actor, EfStore.RootImage image)
        {
            await RefuseStorage(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Instances" WHERE "FoundationReference" = {actor.Id}
                """, ct), "23514");
            Check((await Image(actor)).Actor == image.Actor && (await ReadPhysical(actor)).Revision == image.Revision,
                "the current generated-root protocol refuses an unqualified retirement and preserves its history");
        }
    }

    private sealed record PhysicalRun(long Revision, long UpdatedAt, bool SameTransaction);

    private static async Task Refused(Func<Task> action, string parameter)
    {
        try { await action(); }
        catch (ArgumentException error) when (error.ParamName == parameter) { return; }
        throw new InvalidOperationException("Expected the workflow history query to be refused.");
    }

    private static async Task RequireFormatRefusal(Func<Task> action)
    {
        try { await action(); }
        catch (StoreFormatException) { return; }
        throw new InvalidOperationException("Expected a workflow history format refusal.");
    }

    private static async Task RefuseStorage(Func<Task> action, string state)
    {
        try { await action(); }
        catch (PostgresException error) when (error.SqlState == state) { return; }
        throw new InvalidOperationException("Expected a fenced workflow history write refusal.");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"ok {label}");
    }
}
