using System.Globalization;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Actor = Kernel.WorkflowActor.WorkflowActor;
using PublicKinds = Kernel.Foundation.Contracts.Kinds;

namespace Kernel.Native.Run;

internal static class PostgresRootListChecks
{
    internal static ActorQuery ApprovalQuery()
    {
        var version = new NativeExecutionModel(new WorkflowRegistry()).StateVersion.ToString(CultureInfo.InvariantCulture);
        return new(Actor.ActorType, ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = DirectApprovalWorkflow.Definition, ["version"] = version }, Parked: false);
    }

    internal static async Task<int> RunAsync(bool owned, bool inBatch, PageToken? saved)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootOperators(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var blue = FixedTenantScope.Bind("list-blue");
        var query = ApprovalQuery();
        await using var db = Open();
        if (saved is null)
        {
            var empty = await PostgresFixtureDatabase.IsEmptyAsync(db, ct);
            Check(empty, "Use an empty PostgreSQL database dedicated to the actor-list fixture.");
            await store.CreateAsync(1);
            await admission.InstallAsync(ct);
        }
        var reads = new PostgresRootReads(store, await admission.OpenJournalCursorAsync(ct), 2);
        var foundation = new PostgresPreviewFoundation(admission, reads, Actor.ActorType, (scope, _) => scope == blue);
        var api = await store.OpenPreviewRootReadsAsync(pageSize: 2, cancellationToken: ct);

        if (saved is not null)
        {
            // The cold process opens the projection after the owner lookup columns and journal have been renamed.
            var first = await api.ListAsync(blue, query, cancellationToken: ct);
            var next = await api.ListAsync(blue, query with
            {
                Labels = new Dictionary<string, string> { ["version"] = query.Labels!["version"], ["definition"] = DirectApprovalWorkflow.Definition }
            }, saved, ct);
            var final = await api.ListAsync(blue, query, next.Next, ct);
            var actual = first.Items.Concat(next.Items).Concat(final.Items).Select(v => v.Header.Ref.Id).ToArray();
            Check(first.Items.Count == 2 && next is { Items.Count: 2, Next: not null } && final is { Items.Count: 1, Next: null } &&
                actual.Distinct().Count() == 5 && actual.SequenceEqual(actual.Order(StringComparer.Ordinal)),
                "a saved token crosses a fresh process and three bounded pages with the owner lookup columns and journal unavailable");
            var listed = first.Items[0];
            var point = await api.GetAsync(blue, listed.Header.Ref, ct);
            Check(point is not null && point.Header.Ref == listed.Header.Ref && point.Header.Tenant == listed.Header.Tenant &&
                point.Header.Status == listed.Header.Status && point.Header.Type == listed.Header.Type &&
                point.Header.TypeData == listed.Header.TypeData &&
                query.Labels!.All(label => point.Header.Labels[label.Key] == label.Value) && point.AsOf > 0 &&
                await api.GetAsync(blue, new ActorRef("not a reference"), ct) is null,
                "a cold point lookup reads committed metadata and the database clock with the owner lookup columns and journal unavailable");
            Check(await Refused(blue, query with { Status = ActorStatus.Finished }) &&
                await Refused(blue, query with { Labels = null }) && await Refused(blue, query with { EndedBefore = 10 }) &&
                await Refused(blue, query with { Parked = null }) && await Refused(blue, query with { Type = null }),
                "continuations are bound to every query filter");
            var tampered = new PageToken((saved.Value.Value[0] == 'A' ? 'B' : 'A') + saved.Value.Value[1..]);
            try { await api.ListAsync(blue, query, tampered, ct); throw new InvalidOperationException("Accepted modified token."); }
            catch (ArgumentException) { }
            try { await api.TailAsync(blue, first.Items[0].Header.Ref, saved, ct); throw new InvalidOperationException("Accepted list token on tail."); }
            catch (ArgumentException) { }
            Check(new[] { first, next, final }.All(page => page.AsOf > 0 && page.Items.All(item => item.AsOf == page.AsOf)),
                "each returned page has its own database clock");
            Console.WriteLine($"passed projection restart {Environment.ProcessId}");
            return 0;
        }

        var workflow = new WorkflowRuntime(foundation, api);
        var approvals = new List<ActorRef>();
        for (var i = 0; i < 5; i++) approvals.Add(await Start(blue, "approval-" + i));
        var neighbor = new ActorRef(await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-start"));
        var unusual = await Start(blue, "odd-labels");
        var completed = await Start(blue, "completed", "terminal");
        Check((await api.ListAsync(blue, query, cancellationToken: ct)).Items.Count == 0 &&
            await api.GetAsync(blue, unusual, ct) is null, "admitted creators do not project uncommitted actors");
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build())
            .Add(Wf.Workflow("terminal", Wf.Node<PostgresFlowchartPreview.Approval>("approval")));
        var prepared = await PostgresNativeRootOwner.PreparePartitionAsync(store, "root-list", 0, registry, 16, ct);
        await using (var owner = await prepared.TryActivateAsync(ct) ?? throw new InvalidOperationException("Expected a fresh partition owner."))
        {
            while (await api.GetAsync(blue, completed, ct) is null || await store.ReadFirstRootAsync(PostgresMultiRootClient.Secondary, neighbor.Id, ct) is null) await Pump(owner);
            var end = (await store.ReadFirstRootAsync(blue.Tenant, completed.Id, ct))!;
            var frame = Json.Read<NativeState>(end.State!).Frames.Values.Single().Id;
            var approval = Json.Write(new CompositeSignal(frame, "approve", "done"));
            await Send(blue, completed, "finish", CompositeSignal.Kind, approval);
            var journal = await api.TailAsync(blue, approvals[0], cancellationToken: ct);
            await Send(blue, approvals[0], "suspend", PublicKinds.Suspend);
            while ((await store.ReadFirstRootAsync(blue.Tenant, completed.Id, ct))?.Status != InstanceStatus.Finished ||
                (await store.ReadFirstRootAsync(blue.Tenant, approvals[0].Id, ct))?.Suspended != true) await Pump(owner);
            var all = await Collect(blue, query);
            Check(all.Select(row => row.Header.Ref).ToHashSet().IsSupersetOf(approvals), "public list projects all committed approvals");
            using var header = JsonDocument.Parse(all.Single(row => row.Header.Ref == approvals[0]).Header.TypeData);
            Check(header.RootElement.GetProperty("Suspended").GetBoolean() &&
                (await api.TailAsync(blue, approvals[0], journal.Next, ct)).Items.Count == 0,
                "a journal-silent suspension updates the persisted public metadata");
            Check(all.All(row => row.Park is null), "suspension leaves the parked filter false");
        }

        await Send(blue, unusual, "odd-labels", "unused");
        var image = (await store.ReadFirstRootAsync(blue.Tenant, unusual.Id, ct))!;
        var input = (await admission.ReadPendingDirectAsync(blue.Tenant, unusual.Id, ct)).Single();
        await store.HeartbeatAsync("list-metadata", Defaults.Lease, ct);
        var ownerFence = await store.TakeAsync(0, "list-metadata", Defaults.Lease, cancellationToken: ct)
            ?? throw new InvalidOperationException("Expected the released test partition.");
        var fence = new PartitionFence(0, ownerFence.Epoch, ownerFence.Position);
        var odd = new Dictionary<string, string> { ["\0"] = "\uD800", ["\\u0000"] = "\uFFFD", ["with/slash"] = "\\\0" };
        Check((await store.CommitRootTurnAsync(blue.Tenant, unusual.Id, image, [input], fence, Defaults.Lease, InstanceStatus.Running,
            image.State, image.StateVersion, image.TypeData, odd, null, ct)).Landed, "the fenced writer accepts ordinal labels outside PostgreSQL text");
        foreach (var label in odd)
        {
            var asked = new ActorQuery(Labels: new Dictionary<string, string> { [label.Key] = label.Value });
            var match = await api.ListAsync(blue, asked, cancellationToken: ct);
            Check(match.Items is [var row] && row.Header.Ref == unusual && row.Header.Labels[label.Key] == label.Value,
                "projection filters and returns the original UTF-16 label code units");
        }
        Check((await api.ListAsync(blue, new(Labels: new Dictionary<string, string> { ["\0"] = "\uFFFD" }), cancellationToken: ct)).Items.Count == 0,
            "escaped label values cannot match a different value or a different key");

        var rollbackFilter = new ActorQuery(Labels: new Dictionary<string, string> { ["transient"] = "rollback" });
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var uncommitted = DurableKeyJson.Write(new { image.Model, image.Definition, image.TypeData,
                Labels = rollbackFilter.Labels!.ToArray(), EscapedState = false });
            Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationHeader" = {uncommitted}, "Revision" = "Revision" + 1,
                    "FoundationWriteRevision" = "Revision" + 1
                WHERE "FoundationTenant" = {blue.Tenant} AND "FoundationReference" = {unusual.Id}
                """, ct) == 1, "the database stages a projection change before commit");
            var committed = await api.GetAsync(blue, unusual, ct);
            Check((await api.ListAsync(blue, rollbackFilter, cancellationToken: ct)).Items.Count == 0 &&
                committed is not null && committed.Header.Labels.Count == odd.Count &&
                odd.All(label => committed.Header.Labels.GetValueOrDefault(label.Key) == label.Value),
                "a separate public reader sees only committed projection metadata");
            await transaction.RollbackAsync(ct);
        }
        Check((await api.ListAsync(blue, rollbackFilter, cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, new(Labels: odd), cancellationToken: ct)).Items.Single().Header.Ref == unusual,
            "rollback preserves the prior persisted metadata");

        var terminal = (await api.ListAsync(blue, new(Status: ActorStatus.Finished), cancellationToken: ct)).Items.Single();
        var ended = terminal.Header.EndedAt!.Value;
        Check(await api.GetAsync(blue, completed, ct) is { Header: { Status: ActorStatus.Finished, EndedAt: var pointEnd } } &&
            pointEnd == ended, "point and list expose the same persisted terminal time");
        Check(terminal.Header.Ref == completed && (await api.ListAsync(blue, new(EndedBefore: ended), cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, new(EndedBefore: ended + 1), cancellationToken: ct)).Items is [var finished] && finished.Header.Ref == completed,
            "the persisted end time accepts only a strict end bound");
        Check((await api.ListAsync(blue, query with { Parked = true }, cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, query with { Type = "wrong" }, cancellationToken: ct)).Items.Count == 0,
            "actor type and park state each restrict the list");

        var ready = await api.ListAsync(blue, query, cancellationToken: ct);
        Check(ready is { Items.Count: 2, Next: not null }, "a ready list issues a continuation");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_actor_list""", ct);
        await RequireUnavailable("point", () => api.GetAsync(blue, completed, ct).AsTask());
        await RequireUnavailable("first page", () => api.ListAsync(blue, query, cancellationToken: ct).AsTask());
        await RequireUnavailable("empty page", () => api.ListAsync(blue, query with { Type = "wrong" }, cancellationToken: ct).AsTask());
        await RequireUnavailable("continuation", () => api.ListAsync(blue, query, ready.Next, ct).AsTask());
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_actor_list""", ct);

        var installationSchema = await db.Database.SqlQueryRaw<string>("""
            SELECT ns.nspname AS "Value" FROM pg_catalog.pg_class relation
            JOIN pg_catalog.pg_namespace ns ON ns.oid = relation.relnamespace
            WHERE relation.oid = to_regclass('"Instances"')
            """).SingleAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA fixture_list_shadow;
            CREATE FUNCTION fixture_list_shadow.kernel_project_root_actor_list() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN RETURN NULL; END;
            $fn$;
            """, ct);
        using var identifiers = new NpgsqlCommandBuilder();
        var shadowConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            SearchPath = "fixture_list_shadow," + identifiers.QuoteIdentifier(installationSchema)
        }.ConnectionString;
        var shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(shadowConnection).Options;
        var shadowStore = new EfStore(() => KernelDb.ForPostgresRootOperators(shadowOptions), postgres: true, fenceInBatch: inBatch);
        var shadowReads = await shadowStore.OpenPreviewRootReadsAsync(pageSize: 2, cancellationToken: ct);
        try
        {
            await shadowStore.InstallPreviewRootSchemaAsync(ct);
            throw new InvalidOperationException("Setup accepted a split creation schema.");
        }
        catch (StoreFormatException error) when (error.Message.Contains("active schema", StringComparison.Ordinal)) { }
        Check(await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('fixture_list_shadow."FoundationRootIssuer"') IS NULL
                AND to_regclass('fixture_list_shadow."FoundationActorList"') IS NULL AS "Value"
            """).SingleAsync(ct), "installation refuses a different creation schema without creating preview relations");
        Check((await shadowReads.ListAsync(blue, query, cancellationToken: ct)).Items.Count == 2 &&
            await shadowReads.GetAsync(blue, completed, ct) is { Header.Status: ActorStatus.Finished },
            "an earlier schema's same-named function leaves the installed projection readable");

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TRIGGER kernel_project_root_actor_list ON "Instances" RENAME TO fixture_original_projection;
            """, ct);
        await RequireUnavailable("missing projection trigger", () => api.ListAsync(blue, query, cancellationToken: ct).AsTask());
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER kernel_project_root_actor_list AFTER UPDATE ON "Instances"
                FOR EACH ROW EXECUTE FUNCTION fixture_list_shadow.kernel_project_root_actor_list();
            """, ct);
        await RequireUnavailable("list with a trigger bound to another schema",
            () => shadowReads.ListAsync(blue, query, cancellationToken: ct).AsTask());
        await RequireUnavailable("point with a trigger bound to another schema",
            () => shadowReads.GetAsync(blue, completed, ct).AsTask());
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER kernel_project_root_actor_list ON "Instances";
            ALTER TRIGGER fixture_original_projection ON "Instances" RENAME TO kernel_project_root_actor_list;
            DROP FUNCTION fixture_list_shadow.kernel_project_root_actor_list();
            DROP SCHEMA fixture_list_shadow;
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_require_root_checkpoint_writer""", ct);
        await RequireUnavailable("disabled checkpoint guard", () => api.ListAsync(blue, query, cancellationToken: ct).AsTask());
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_require_root_checkpoint_writer""", ct);
        Check(await api.GetAsync(blue, completed, ct) is { Header.Status: ActorStatus.Finished } &&
            (await api.ListAsync(blue, query, ready.Next, ct)).Items.Count == 2,
            "point and list resume with the trusted source restored and no intervening checkpoint writes");

        static async Task RequireUnavailable(string read, Func<Task> call)
        {
            try
            {
                await call();
                throw new InvalidOperationException($"Actor projection accepted {read} while a required trigger was unavailable.");
            }
            catch (StoreFormatException error) when (error.Message.Contains("required trigger", StringComparison.Ordinal))
            {
                Console.WriteLine($"ok actor projection refuses {read} while a required trigger is unavailable");
            }
        }

        await db.Database.ExecuteSqlRawAsync("""UPDATE "KernelStoreVersions" SET "StoreSchema" = "StoreSchema" + 1""", ct);
        try
        {
            await api.GetAsync(blue, completed, ct);
            throw new InvalidOperationException("Point lookup accepted an incompatible store format.");
        }
        catch (StoreFormatException) { }
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationActorList" RENAME COLUMN "Header" TO "FutureHeader" """, ct);
        try
        {
            await api.GetAsync(blue, completed, ct);
            throw new InvalidOperationException("Point lookup accepted an incompatible projection shape.");
        }
        catch (StoreFormatException) { }
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationActorList" RENAME COLUMN "FutureHeader" TO "Header";
            ALTER TABLE "FoundationActorList" ALTER COLUMN "Reference" TYPE bytea USING convert_to("Reference", 'UTF8');
            """, ct);
        try
        {
            await api.GetAsync(blue, completed, ct);
            throw new InvalidOperationException("Point lookup accepted a changed reference type.");
        }
        catch (StoreFormatException) { }
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationActorList" ALTER COLUMN "Reference" TYPE text COLLATE "C" USING convert_from("Reference", 'UTF8');
            UPDATE "KernelStoreVersions" SET "StoreSchema" = "StoreSchema" - 1;
            """, ct);
        Check(await api.GetAsync(blue, completed, ct) is { Header.Status: ActorStatus.Finished },
            "point lookup requires a readable store format");

        // Backfill an older database, then invalidate the former checkpoint and journal lookup paths.
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER kernel_project_root_actor_list ON "Instances";
            DROP TABLE "FoundationActorList";
            """, ct);
        await admission.InstallAsync(ct);
        await admission.InstallAsync(ct);
        var page = await api.ListAsync(blue, query, cancellationToken: ct);
        Check(page.Items.Count == 2 && page.Next is not null && (await Collect(blue, query)).Count == 5,
            "idempotent installation backfills existing public metadata");
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" RENAME COLUMN "FoundationReference" TO "FixtureOwnerReference";
            ALTER TABLE "Instances" RENAME COLUMN "State" TO "FixtureOwnerState";
            ALTER TABLE "Journal" RENAME TO "FixtureJournalRows";
            """, ct);
        Check(await api.GetAsync(blue, neighbor, ct) is null, "a foreign reference is a miss on the point projection");
        await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-list", neighbor.Id, page.Next!.Value.Value);
        Console.WriteLine($"projection-bookmark {page.Next!.Value.Value}");
        Console.WriteLine($"passed projection birth {Environment.ProcessId}");
        return 0;

        async Task<ActorRef> Start(BoundScope scope, string id, string definition = DirectApprovalWorkflow.Definition) =>
            (await workflow.StartAsync(scope, id, NativeExecutionModel.Start(definition), cancellationToken: ct)).Actor!.Value;
        async Task Send(BoundScope scope, ActorRef actor, string id, string kind, string? payload = null) =>
            Check((await foundation.SendAsync(scope, new Target.Ref(actor), id, kind, payload, cancellationToken: ct)).Outcome == AdmissionOutcome.Accepted,
                "input admitted through the public surface");
        async Task Pump(IScopeActivation owner)
        {
            await owner.WokenAsync(null, ct);
            await owner.RunAsync(ct);
        }
        async Task<List<ActorView>> Collect(BoundScope scope, ActorQuery filter)
        {
            var items = new List<ActorView>();
            PageToken? from = null;
            do
            {
                var read = await api.ListAsync(scope, filter, from, ct);
                items.AddRange(read.Items);
                from = read.Next;
            } while (from is not null);
            return items;
        }
        async Task<bool> Refused(BoundScope scope, ActorQuery filter)
        {
            try { await api.ListAsync(scope, filter, saved, ct); return false; }
            catch (ArgumentException) { return true; }
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
