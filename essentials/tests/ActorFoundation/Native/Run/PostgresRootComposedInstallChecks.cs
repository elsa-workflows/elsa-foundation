using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

// S4 (#4229): a composed install whose stored definitions are checked. An unshipped set carries retirement, terminal selection,
// Park inspection and workflow history. Reinstalling over data keeps every definition; a narrower earlier install is refused.
// A root retired through the store's own path loses its history row in the same commit, and the history reader is bound to
// its schema. Seeded rows go through the guarded checkpoint update; retirement, admission and reads are the store's own paths.
internal static class PostgresRootComposedInstallChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 96 };
    private const string NarrowSchema = "composed_narrow";
    private const string ShadowSchema = "composed_shadow";
    private const string Model = "composed";
    private const string Definition = "definition-a";

    private sealed record Header(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = RootProfiles.Formats[StoreFormat.RootRetirement] | RootProfiles.Formats[StoreFormat.RootParkInspection];
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var stop = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the composed-install fixture uses an empty PostgreSQL database");
        Check(db.SupportsRootWorkflowHistory && db.UsesRootRetirement && db.UsesRootParkInspection && db.UsesRootTerminalSelection,
            "the composed set carries retirement, terminal selection, Park inspection and workflow history");
        await using (var shipped = KernelDb.ForPostgresRootRetirement(options))
            Check(!shipped.SupportsRootWorkflowHistory, "the shipped retirement profile still refuses workflow history");

        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        await cold.InstallRootWorkflowHistoryAsync(stop);
        await admission.InstallAsync(stop);
        await cold.InstallRootWorkflowHistoryAsync(stop);
        Check(true, "a second install over the composed install keeps every definition");

        // A root of the real store path: started, finished, then retired by a selected Delete.
        var tenant = EmbeddedTenant.Bind("compose");
        var cursor = await admission.OpenJournalCursorAsync(stop);
        var reads = new PostgresRootReads(cold, cursor, 12);
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, tenant), stop, (scope, _) => ReferenceEquals(scope, tenant));
        var runtime = new WorkflowRuntime(ingress, reads);
        var actorType = new PublicActor([new NativeExecutionModel(new WorkflowRegistry().Add(Wf.Workflow("finish", Wf.Sequence("instant"))))]);
        var target = (await runtime.StartAsync(tenant, "retired", NativeExecutionModel.Start("finish"), cancellationToken: stop)).Actor!.Value;
        var kept = (await runtime.StartAsync(tenant, "kept", NativeExecutionModel.Start("finish"), cancellationToken: stop)).Actor!.Value;
        EfStore.RootImage finished;
        await using (var owner = await Own("first"))
        {
            finished = await Drive(owner, target);
            await Drive(owner, kept);
        }
        var type = (await cold.ReadRootActorAsync(tenant.Tenant, target.Id, stop))!.Value.Item.Type;
        Check(await HistoryRows(target) == 1 && (await cold.ReadRootWorkflowHistoryAsync(tenant.Tenant, type, null, null, null, null, null, null, 10, stop))
                .Items.Select(item => item.Reference).Order(StringComparer.Ordinal).SequenceEqual(new[] { target.Id, kept.Id }.Order(StringComparer.Ordinal)),
            "both finished roots of the real path are in workflow history");

        var selection = (await reads.GetAsync(tenant, target, stop))!.Selection;
        Check((await ingress.SendAsync(tenant, new Contract.Target.Ref(target), "delete-retired", Contract.Kinds.Delete,
            JsonSerializer.Serialize(new Contract.OperatorCommand(selection)), cancellationToken: stop)).Outcome == Contract.AdmissionOutcome.Accepted,
            "a selected Delete of the finished root is admitted");
        var command = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, target.Id, stop))!;
        await SuppressedHistoryDeleteRollsBack();
        await using (var owner = await Own("second"))
            for (var turn = 0; !(await Inbox(command)).Done; turn++)
            {
                if (turn == 50) throw new InvalidOperationException("The retirement did not land.");
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
        Check(await HistoryRows(target) == 0 && await HistoryRows(kept) == 1 &&
            (await cold.ReadJournalOfAsync(0, finished.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }],
            "retirement deletes only the retired root's history row, and the residue check admits the deletion marker");
        Check((await cold.ReadRootWorkflowHistoryAsync(tenant.Tenant, type, null, null, null, null, null, null, 10, stop)).Items is [{ Reference: var left }] &&
            left == kept.Id, "workflow history lists the kept root alone");

        // Seed a running root, reinstall over the data, then park it: the Park metadata trigger fires after the reinstall.
        var header = DurableKeyJson.Write(new Header(Model, Definition, "{}", [new("definition", Definition)], false));
        db.Instances.Add(new InstanceEntity { Id = "0/parked", Partition = 0, Model = Model, Definition = Definition, Status = InstanceStatus.Running, State = "{}" });
        await db.SaveChangesAsync(stop);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
            VALUES ('ref-parked', {tenant.Tenant}, '0/parked', 0, 1, 'creator-parked', 'seed');
            UPDATE "Instances" SET "FoundationReference" = 'ref-parked', "FoundationTenant" = {tenant.Tenant}, "FoundationHeader" = {header},
                "Revision" = 1, "FoundationWriteRevision" = 1 WHERE "Id" = '0/parked';
            """, stop);
        await admission.InstallAsync(stop);
        await cold.InstallRootWorkflowHistoryAsync(stop);
        Check(true, "reinstalling over roots, history and a retired marker keeps every definition");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {DurableKeyJson.Write("parked on purpose")},
                "Revision" = 2, "FoundationWriteRevision" = 2, "FoundationParkAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            WHERE "Id" = '0/parked'
            """, stop);
        Check(await cold.ReadRootActorAsync(tenant.Tenant, "ref-parked", stop) is ({ ParkAt: > 0, ParkFault: "parked on purpose", TerminalRevision: null }, > 0),
            "the Park metadata trigger projects a Park made after the reinstall");
        Check((await cold.ReadRootWorkflowHistoryAsync(tenant.Tenant, Model, Definition, null, null, null, null, null, 10, stop)).Items is [{ Reference: "ref-parked" }],
            "workflow history records the parked root through the reinstalled projector");

        await ShadowedHistoryRefused();
        await NarrowerInstallRefused();

        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=history-refusal.invalid").Options;
        try
        {
            await new EfStore(() => KernelDb.ForPostgresRootRetirement(unreachable), postgres: true).InstallRootWorkflowHistoryAsync(stop);
            throw new InvalidOperationException("The shipped retirement profile installed workflow history.");
        }
        catch (StoreFormatException error) when (error.Message == "This store profile does not carry workflow history.")
        {
            Check(true, "the history install refuses the shipped retirement profile before it opens a connection");
        }
        return 0;

        // A trigger that skips the history delete leaves the row in place, so the residue check refuses the deletion marker
        // and the whole retirement rolls back: the history row leaves only in the retirement's own commit.
        async Task SuppressedHistoryDeleteRollsBack()
        {
            var store = Store();
            await store.BindWorkerPartitionAsync(0, stop);
            var lease = (await store.TakeAsync(0, "suppressed-history", Defaults.Lease, stop))!;
            var instance = new InstanceRow(finished.Actor.Id, finished.Model, finished.Definition, finished.Status, finished.State!, finished.Revision, null,
                Creation: finished.Actor.Creation);
            var retirement = new TurnCommit(instance.Id, CreationProtocol.Retire(instance), finished.Revision,
                [], [command.InboxKey], [], [], [], """["delete"]""", Unsubscribe: true, Delete: true) { FoundationRootOperator = command };
            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION fixture_keep_history() RETURNS trigger LANGUAGE plpgsql AS $fn$ BEGIN RETURN NULL; END $fn$;
                    CREATE TRIGGER fixture_keep_history BEFORE DELETE ON "FoundationWorkflowHistory"
                    FOR EACH ROW EXECUTE FUNCTION fixture_keep_history();
                    """, stop);
                var result = await store.CommitAsync(new(0, lease.Epoch, lease.Position, Defaults.Lease, false, [retirement], [], []), stop);
                Check(!result.Landed && await HistoryRows(target) == 1 && !(await Inbox(command)).Done &&
                    await cold.ReadFirstRootAsync(tenant.Tenant, target.Id, stop) is { Revision: var revision } && revision == finished.Revision,
                    "a retirement whose history delete is suppressed rolls back the checkpoint, inbox and history row");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS fixture_keep_history ON "FoundationWorkflowHistory";
                    DROP FUNCTION IF EXISTS fixture_keep_history();
                    """, stop);
                await store.ReleaseAsync(0, lease.Epoch, stop);
            }
        }

        // The history reader encodes its filters itself, so no kernel_actor_list_filter of an earlier search-path schema, an
        // overload with a default or a variadic one included, takes part in a read. It refuses an actor list, kernel marker,
        // history table or history format that such a schema shadows.
        async Task ShadowedHistoryRefused()
        {
            var schema = await db.Database.SqlQueryRaw<string>("""SELECT current_schema() AS "Value" """).SingleAsync(stop);
            using var identifiers = new NpgsqlCommandBuilder();
            var shadowed = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
                { SearchPath = ShadowSchema + "," + identifiers.QuoteIdentifier(schema) }.ConnectionString).Options;
            var shadow = new EfStore(() => KernelDb.ForUnshippedComposition(shadowed, Composed, capabilities), postgres: true);
            Check(type != Model && (await Typed(shadow)).Items is [{ Reference: "ref-parked" }],
                "the history read of the parked root's type lists it alone, beside a kept root of another type");
            // Each shadow filter returns the kept root's type, so a read whose call it captured would list the kept root.
            await db.Database.ExecuteSqlRawAsync($"""CREATE SCHEMA {ShadowSchema}; CREATE TABLE {ShadowSchema}.fixture_capture ("Type" jsonb)""", stop);
            Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO composed_shadow.fixture_capture
                SELECT "Type" FROM "FoundationActorList" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {kept.Id}
                """, stop) == 1, "the shadow filters capture the kept root's type");
            foreach (var signature in new[] { "source_json text", "source_json text, extra integer DEFAULT 0", "VARIADIC source_json text[]" })
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    CREATE FUNCTION {ShadowSchema}.kernel_actor_list_filter({signature}) RETURNS jsonb LANGUAGE sql STABLE
                        AS $fn$ SELECT "Type" FROM {ShadowSchema}.fixture_capture $fn$;
                    """, stop);
                Check((await Typed(shadow)).Items is [{ Reference: "ref-parked" }] && (await History(shadow)).Items is [{ Reference: "ref-parked" }],
                    $"a shadow kernel_actor_list_filter({signature}) has no effect on the history read");
                await db.Database.ExecuteSqlRawAsync($"DROP FUNCTION {ShadowSchema}.kernel_actor_list_filter", stop);
            }
            foreach (var (relation, label) in new[] { ("FoundationActorList", "actor list"), ("KernelStoreVersions", "kernel marker"),
                ("FoundationWorkflowHistory", "history table"), ("FoundationWorkflowHistoryFormat", "history format") })
            {
                await db.Database.ExecuteSqlRawAsync($"""CREATE TABLE {ShadowSchema}."{relation}" AS TABLE "{relation}" """, stop);
                await Refused(() => History(shadow), "the history reader refuses a shadowed " + label);
                await db.Database.ExecuteSqlRawAsync($"""DROP TABLE {ShadowSchema}."{relation}" """, stop);
            }
            await db.Database.ExecuteSqlRawAsync($"""DROP SCHEMA {ShadowSchema} CASCADE""", stop);
            Check((await History(shadow)).Items.Count == 1, "the history reader reads again once nothing shadows its schema");
        }

        // A narrower set installs first in its own schema; the set with Park arrivals and recovery then reinstalls over it with
        // the same format. Its wider KeptBy CHECK and inspectable index predicate differ from the stored ones, so it is refused,
        // and the recovery columns it added roll back. (The narrower set already records the Park revision, S5b-1.)
        async Task NarrowerInstallRefused()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + NarrowSchema, stop);
            var narrow = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = NarrowSchema }.ConnectionString).Options;
            await using (var created = KernelDb.ForUnshippedComposition(narrow, Composed, capabilities))
                await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            await new EfStore(() => KernelDb.ForUnshippedComposition(narrow, Composed, capabilities), postgres: true).CreateAsync(1);
            await new PostgresRootAdmission(() => KernelDb.ForUnshippedComposition(narrow, Composed, capabilities), TimeSpan.FromHours(1)).InstallAsync(stop);
            var wider = new PostgresRootAdmission(() => KernelDb.ForUnshippedComposition(narrow, Composed,
                capabilities | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery), TimeSpan.FromHours(1));
            try
            {
                await wider.InstallAsync(stop);
                throw new InvalidOperationException("A wider install kept the narrower definitions.");
            }
            catch (StoreFormatException error) when (error.Message.StartsWith("The installed root definitions differ", StringComparison.Ordinal))
            {
                Check(error.Message.Contains("the FoundationKeptBy CHECK", StringComparison.Ordinal),
                    "a wider install over a narrower one is refused for its KeptBy CHECK");
                Check(error.Message.Contains("IX_FoundationRootInbox_Inspectable", StringComparison.Ordinal),
                    "a wider install over a narrower one is refused for its inspectable index predicate");
            }
            await using (var check = KernelDb.ForUnshippedComposition(narrow, Composed, capabilities))
                Check(await check.Database.SqlQueryRaw<bool>("""
                    SELECT to_regclass('"Instances"') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute
                        WHERE attrelid = to_regclass('"Instances"') AND attname = 'FoundationParkGeneration') AS "Value"
                    """).SingleAsync(stop), "the refused install rolls back the columns it added");
            await new PostgresRootAdmission(() => KernelDb.ForUnshippedComposition(narrow, Composed, capabilities), TimeSpan.FromHours(1)).InstallAsync(stop);
            Check(true, "the narrower set still reinstalls over its own definitions");

            // The same set reinstalling over a UNIQUE copy of its inspectable index, then over an invalid one, is refused.
            var same = new PostgresRootAdmission(() => KernelDb.ForUnshippedComposition(narrow, Composed, capabilities), TimeSpan.FromHours(1));
            await using var tamper = KernelDb.ForUnshippedComposition(narrow, Composed, capabilities);
            var definition = await tamper.Database.SqlQueryRaw<string>("""
                SELECT pg_catalog.pg_get_indexdef(to_regclass('"IX_FoundationRootInbox_Inspectable"')) AS "Value"
                """).SingleAsync(stop);
            await tamper.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_FoundationRootInbox_Inspectable\"; " +
                definition.Replace("CREATE INDEX", "CREATE UNIQUE INDEX", StringComparison.Ordinal), stop);
            await IndexRefused(same, "a reinstall over a UNIQUE inspectable index is refused");
            await tamper.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_FoundationRootInbox_Inspectable\"; " + definition, stop);
            await tamper.Database.ExecuteSqlRawAsync("""
                UPDATE pg_catalog.pg_index SET indisvalid = false WHERE indexrelid = to_regclass('"IX_FoundationRootInbox_Inspectable"')
                """, stop);
            await IndexRefused(same, "a reinstall over an invalid inspectable index is refused");
            await tamper.Database.ExecuteSqlRawAsync("""
                UPDATE pg_catalog.pg_index SET indisvalid = true WHERE indexrelid = to_regclass('"IX_FoundationRootInbox_Inspectable"')
                """, stop);
            await same.InstallAsync(stop);
            Check(true, "the restored inspectable index reinstalls cleanly");
        }

        async Task IndexRefused(PostgresRootAdmission admission, string label)
        {
            try
            {
                await admission.InstallAsync(stop);
                throw new InvalidOperationException("Expected an index definition refusal: " + label);
            }
            catch (StoreFormatException error) when (error.Message.StartsWith("The installed root definitions differ", StringComparison.Ordinal) &&
                error.Message.Contains("IX_FoundationRootInbox_Inspectable", StringComparison.Ordinal))
            {
                Check(true, label);
            }
        }

        Task<EfStore.RootWorkflowHistoryPage> History(EfStore store) =>
            store.ReadRootWorkflowHistoryAsync(tenant.Tenant, Model, Definition, null, null, null, null, null, 10, stop);

        Task<EfStore.RootWorkflowHistoryPage> Typed(EfStore store) =>
            store.ReadRootWorkflowHistoryAsync(tenant.Tenant, Model, null, null, null, null, null, null, 10, stop);

        async Task<int> HistoryRows(Contract.ActorRef root) => await db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM "FoundationWorkflowHistory" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {root.Id}
            """).SingleAsync(stop);

        async Task<IScopeActivation> Own(string node)
        {
            var store = Store();
            var scope = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), actorType);
            var host = new DurableHost([new Worker(node, 0, store, adapter, new WorkerOptions { Partitions = 1 })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Contract.ActorRef reference)
        {
            for (var turn = 0; turn < 50; turn++)
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
                if (await cold.ReadFirstRootAsync(tenant.Tenant, reference.Id, stop) is { Status: InstanceStatus.Finished } image)
                    return image;
            }
            throw new InvalidOperationException("The root did not finish.");
        }

        async Task<MessageEntity> Inbox(Message message)
        {
            await using var read = Open();
            return await read.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == message.InboxKey.Scope && row.Id == message.InboxKey.Id, stop);
        }
    }

    private static async Task Refused<T>(Func<Task<T>> read, string label)
    {
        try { await read(); }
        catch (StoreFormatException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected a refusal: " + label);
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("ok " + label);
    }
}
