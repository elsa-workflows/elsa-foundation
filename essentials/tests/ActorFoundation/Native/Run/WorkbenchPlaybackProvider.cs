using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using ContractKinds = Kernel.Foundation.Contracts.Kinds;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    private static async Task VerifyPlaybackProviderAsync(string connection, string? sqlite, string run, bool descendants, CancellationToken token)
    {
        var postgres = sqlite is null;
        var options = PlaybackOptions(connection, sqlite);
        var replacement = postgres && descendants ? new PlaybackCommitReplacement() : null;
        var validationFailure = replacement is null ? null : new PlaybackValidationFailure();
        var ownerOptions = replacement is null ? options : new DbContextOptionsBuilder<KernelDb>(options)
            .AddInterceptors(replacement, validationFailure!).Options;
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => OpenPlayback(ownerOptions, postgres, definitions, descendants);
        var reads = new EfStore(Open, postgres);
        var query = new ActorPlaybackQuery(2, 0, null, null);
        var first = await reads.ReadRootPlaybackAsync("default", run, query, token);
        Require(first is { Availability: ActorPlaybackAvailability.Available, Count: > 0 }, "the retained provider has indexed authored activity");
        if (descendants)
        {
            using var observed = JsonDocument.Parse(first!.Items[0].Observation);
            var rootActivity = observed.RootElement.GetProperty("Frame").GetString()!;
            var ranked = new ActorPlaybackQuery(2, 2, null, rootActivity, first.UpperSequence, first.RetainedAfterSequence)
                { IncludeDescendants = true };
            var observer = new PlaybackScopePlanObserver(postgres);
            var intercepted = new DbContextOptionsBuilder<KernelDb>(options).AddInterceptors(observer).Options;
            var monitored = new EfStore(() => OpenPlayback(intercepted, postgres, definitions, descendants), postgres);
            var rankedPage = await monitored.ReadRootPlaybackAsync("default", run, ranked, token);
            Require(rankedPage is { Availability: ActorPlaybackAvailability.Available, SelectedPosition: 2 } &&
                rankedPage.Items.Any(item => item.Position == 2) && observer.Checked == 3,
                "the actual bounded scoped queries seek a nonzero frozen rank in a retained published run");
            if (postgres)
                await RefusePostgresPlaybackDriftAsync(Open, run, ranked, token);
        }
        if (postgres)
            await RefusePlaybackNamespaceReplacementAsync(options, run, descendants, token);

        const string node = "playback-observation-probe";
        var actor = new PlaybackObservationProbe();
        var sqliteHost = sqlite is null ? null : descendants
            ? SqliteEmbeddedFoundation.ForPlaybackDescendantsQualification(sqlite, node, actor.Type)
            : SqliteEmbeddedFoundation.ForPlaybackQualification(sqlite, node, actor.Type);
        if (sqliteHost is not null) await sqliteHost.InstallShippedAsync(token);
        var requests = postgres ? PostgresPreviewHosting.CreateRequests(Open, node, actor.Type) : null;
        IHostedService owner = sqliteHost is null
            ? new PostgresPreviewHostedService(Open, node, actor, Escalate, requests: requests)
            : sqliteHost.CreateHostedService(actor, Escalate);
        await using var lifetime = (IAsyncDisposable)owner;
        var ingress = sqliteHost is null
            ? (IIngress)await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type, requests, cancellationToken: token)
            : await sqliteHost.OpenIngressAsync(token);
        await owner.StartAsync(token);
        var scope = EmbeddedTenant.Bind("default");
        var admitted = await ingress.SendAsync(scope, new Target.Start(new Contract.StartSpec(actor.Type, "playback")), "start",
            Contract.Kinds.Start, cancellationToken: token);
        if (admitted is not { Outcome: AdmissionOutcome.Accepted, Actor: { } exact })
            throw new InvalidOperationException("The malformed-observation probe was not admitted.");

        var initial = await ObserveAfterAsync(0);
        var prefix = await SendAsync("diagnostics",
            """["b:f900:unmarked","{\"NativeStructure\":1,\"Kind\":2,\"At\":1,\"Frame\":\"f900\",\"Node\":\"lookalike\"}"]""",
            initial.LatestSequence);
        Require(initial is { Availability: ActorPlaybackAvailability.Available, Count: 1 } &&
            prefix is { Availability: ActorPlaybackAvailability.Available, Count: 1 },
            "unmarked diagnostic strings leave Playback membership unchanged");
        if (replacement is not null)
            prefix = await VerifyAtomicProjectionAsync(prefix, replacement, validationFailure!);
        var clean = prefix;
        if (descendants)
        {
            var scopedQuery = new ActorPlaybackQuery(2, null, null, "f1") { IncludeDescendants = true };
            var original = await ReadScopeAsync(scopedQuery);
            var orphan = await SendAsync("orphan", """
                [{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f9","Node":"orphan","Parent":"f10"}]
                """, prefix.LatestSequence);
            clean = await SendAsync("late-parent", """
                [{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f10","Node":"late-parent","Parent":"f1"}]
                """, orphan.LatestSequence);
            var unavailable = await ReadScopeAsync(scopedQuery);
            var frozenScope = await ReadScopeAsync(scopedQuery with
            {
                UpperSequence = original!.UpperSequence,
                RetainedAfterSequence = original.RetainedAfterSequence
            });
            var exactOrphan = await ReadScopeAsync(scopedQuery with { Activity = "f9", IncludeDescendants = false });
            Require(original is { Availability: ActorPlaybackAvailability.Available } && original.Count == prefix.Count &&
                frozenScope is { Availability: ActorPlaybackAvailability.Available } && frozenScope.Count == prefix.Count &&
                clean is { Availability: ActorPlaybackAvailability.Available } && clean.Count == prefix.Count + 2 &&
                exactOrphan is { Availability: ActorPlaybackAvailability.Available, Count: 1 } &&
                unavailable is { Availability: ActorPlaybackAvailability.Unavailable, Reason: "scope-ancestry-unknown", Items.Count: 0 },
                "a late parent never repairs the orphan's committed ancestry or disturbs earlier frozen, exact and global membership");

            Task<ActorPlaybackPage?> ReadScopeAsync(ActorPlaybackQuery scopeQuery) =>
                reads.ReadRootPlaybackAsync(scope.Tenant, exact.Id, scopeQuery, token);
        }

        string[] malformed =
        [
            """{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f900","Node":["bad"]}""",
            """{"NativeStructure":1,"Kind":2,"At":1e3,"Frame":"f901","Node":"bad"}""",
            """{"NativeStructure":1,"Kind":2,"At":253402300800000,"Frame":"f902","Node":"bad"}""",
            """{"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f903","Node":"bad"}""",
            """{"NativeStructure":1,"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f904","Node":"bad"}"""
        ];
        var stale = clean;
        for (var index = 0; index < malformed.Length; index++)
            stale = await SendAsync($"marked-{index}", "[" + malformed[index] + "]", stale.LatestSequence);

        await using var write = Open();
        var invalid = await write.Database.SqlQuery<long>($"""
            SELECT CAST(COUNT(*) AS bigint) AS "Value" FROM "FoundationJournalPlaybackInvalid" invalid
            JOIN "FoundationRootBirths" birth ON birth."Partition" = invalid."Partition" AND birth."Actor" = invalid."Actor"
            WHERE birth."Tenant" = {scope.Tenant} AND birth."Reference" = {exact.Id} AND birth."Creation" = 1
            """).SingleAsync(token);
        Require(invalid == malformed.Length, "every malformed marked observation gets a retained refusal receipt");
        var frozen = await reads.ReadRootPlaybackAsync(scope.Tenant, exact.Id,
            query with { UpperSequence = clean.UpperSequence, RetainedAfterSequence = clean.RetainedAfterSequence }, token);
        Require(stale is { Availability: ActorPlaybackAvailability.Unavailable, Reason: "occurrence-observation-unknown", Items.Count: 0 } &&
            frozen is { Availability: ActorPlaybackAvailability.Available } && frozen.Count == clean.Count && frozen.LatestSequence > frozen.UpperSequence,
            "malformed marked observations refuse fresh admission while earlier frozen membership remains playable");
        await owner.StopAsync(token);

        async Task<ActorPlaybackPage> SendAsync(string id, string payload, long after)
        {
            var delivery = await ingress.SendAsync(scope, new Target.Ref(exact), id, "record", payload, cancellationToken: token);
            Require(delivery.Outcome == AdmissionOutcome.Accepted, "the owned probe admits the journal observation");
            return await ObserveAfterAsync(after);
        }

        async Task<ActorPlaybackPage> ObserveAfterAsync(long after)
        {
            while (true)
            {
                var page = await reads.ReadRootPlaybackAsync(scope.Tenant, exact.Id, query, token);
                if (page is not null && page.LatestSequence > after) return page;
                await Task.Delay(25, token);
            }
        }

        async Task<ActorPlaybackPage> VerifyAtomicProjectionAsync(ActorPlaybackPage before, PlaybackCommitReplacement drift,
            PlaybackValidationFailure failure)
        {
            await using var observer = OpenPlayback(options, postgres: true, definitions, descendants: true);
            var installed = await ReadPlaybackProjectorAsync(observer, token);
            const string completeMembership = """WHERE v."Known" = 1 OR a."Depth" = 0""";
            var selfOnly = installed.Replace(completeMembership, """WHERE a."Depth" = 0""", StringComparison.Ordinal);
            Require(selfOnly != installed, "the partial projector keeps base observations and self membership while omitting ancestors");
            await observer.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA playback_projection_shadow;
                CREATE TABLE playback_projection_shadow."FoundationJournalPlaybackScopes"
                    AS TABLE public."FoundationJournalPlaybackScopes";
                """, token);
            try
            {
                before = await VerifyRaceAsync("partial-playback-append", "f2", selfOnly, before);
                return await VerifyRaceAsync("shadow-playback-append", "f3", """
                    ALTER FUNCTION public.kernel_project_root_playback_scopes()
                        SET search_path TO pg_catalog, playback_projection_shadow, public, pg_temp;
                    """, before);
            }
            finally
            {
                await observer.Database.ExecuteSqlRawAsync("DROP SCHEMA playback_projection_shadow CASCADE", token);
            }

            async Task<ActorPlaybackPage> VerifyRaceAsync(string input, string frame, string changed, ActorPlaybackPage current)
            {
                var baseline = await ReadPhysicalAsync();
                try
                {
                    var rejected = failure.ExpectRejection();
                    // Change the function only after the real owner's journal pin, on a separate connection.
                    drift.Arm(ct => observer.Database.ExecuteSqlRawAsync(changed, ct));
                    var body = JsonSerializer.Serialize(new[] { new { NativeStructure = 1, Kind = 2, At = 1000, Frame = frame, Node = frame, Parent = "f1" } });
                    var delivery = await ingress.SendAsync(scope, new Target.Ref(exact), input, "record", body, cancellationToken: token);
                    Require(delivery.Outcome == AdmissionOutcome.Accepted, $"the owner admits the {input} input");
                    await rejected.WaitAsync(token);
                    Require(await ReadPhysicalAsync() == baseline, $"the {input} rolls back the exact fence, checkpoint, journal and index rows");
                }
                finally
                {
                    await observer.Database.ExecuteSqlRawAsync("ALTER FUNCTION public.kernel_project_root_playback_scopes() RESET ALL", token);
                    await observer.Database.ExecuteSqlRawAsync(installed, token);
                }

                var recovered = await ObserveAfterAsync(current.LatestSequence);
                var committed = await ReadPhysicalAsync();
                Require(recovered.Count == current.Count + 1 && committed.Ranges == baseline.Ranges + 1 &&
                    committed.Observations == baseline.Observations + 1 && committed.Memberships == baseline.Memberships + 2 &&
                    committed.Revision == baseline.Revision + 1,
                    $"the real owner retries {input} and commits the exact child and ancestor membership once");
                return recovered;
            }

            Task<PlaybackWriteState> ReadPhysicalAsync() => observer.Database.SqlQuery<PlaybackWriteState>($"""
                SELECT p."Position", state."Revision",
                    (SELECT COUNT(*) FROM "FoundationSequencedJournal" range_row
                        WHERE range_row."Partition" = birth."Partition" AND range_row."Actor" = birth."Actor") AS "Ranges",
                    (SELECT COUNT(*) FROM "FoundationJournalPlayback" observation
                        WHERE observation."Partition" = birth."Partition" AND observation."Actor" = birth."Actor") AS "Observations",
                    (SELECT COUNT(*) FROM "FoundationJournalPlaybackScopes" member
                        WHERE member."Partition" = birth."Partition" AND member."Actor" = birth."Actor") AS "Memberships"
                FROM "FoundationRootBirths" birth
                JOIN "Instances" state ON state."Partition" = birth."Partition" AND state."Id" = birth."Actor"
                JOIN "Partitions" p ON p."Id" = birth."Partition"
                WHERE birth."Tenant" = {scope.Tenant} AND birth."Reference" = {exact.Id} AND birth."Creation" = 1
                """).SingleAsync(token);
        }

        static void Escalate(string message) => throw new InvalidOperationException(message);
    }

    private sealed record PlaybackWriteState(long Position, long Revision, long Ranges, long Observations, long Memberships);

    private sealed class PlaybackCommitReplacement : DbCommandInterceptor
    {
        private Func<CancellationToken, Task>? replace;

        internal void Arm(Func<CancellationToken, Task> operation) => Volatile.Write(ref replace, operation);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(RowGroups.Journal, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref replace, null) is { } mutate)
                await mutate(cancellationToken);
            return result;
        }
    }

    private sealed class PlaybackValidationFailure : DbTransactionInterceptor
    {
        private TaskCompletionSource? rejected;

        internal Task ExpectRejection()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref rejected, signal);
            return signal.Task;
        }

        public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is PostgresException { SqlState: "23514", ConstraintName: EfStore.PlaybackScopeGuard })
                Interlocked.Exchange(ref rejected, null)?.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class PlaybackScopePlanObserver(bool postgres) : DbCommandInterceptor
    {
        internal int Checked { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            (string Name, string SqliteKey) expected = command.CommandText switch
            {
                var sql when sql.Contains("/* playback scoped rank */", StringComparison.Ordinal) =>
                    ("kernel_playback_scope_rank", "Ordinal>? AND Ordinal<?"),
                var sql when sql.Contains("/* playback scoped retained bound */", StringComparison.Ordinal) =>
                    ("kernel_playback_scope_sequence", "Sequence>? AND Sequence<?"),
                var sql when sql.Contains("/* playback scoped ancestry */", StringComparison.Ordinal) =>
                    ("kernel_playback_scope_unknown", "kernel_playback_scope_unknown"),
                _ => default
            };
            if (expected.Name is null) return result;

            await using var explain = command.Connection!.CreateCommand();
            explain.Transaction = command.Transaction;
            explain.CommandText = (postgres ? "EXPLAIN (COSTS OFF) " : "EXPLAIN QUERY PLAN ") + command.CommandText;
            foreach (DbParameter parameter in command.Parameters)
            {
                var copy = explain.CreateParameter();
                copy.ParameterName = parameter.ParameterName;
                copy.Value = parameter.Value;
                copy.DbType = parameter.DbType;
                explain.Parameters.Add(copy);
            }

            var plan = new List<string>();
            await using var reader = await explain.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                plan.Add(reader.GetString(postgres ? 0 : 3));
            var index = postgres ? expected.Name : expected.SqliteKey;
            Require(plan.Any(line => line.Contains(index, StringComparison.Ordinal)) &&
                !plan.Any(line => line.Contains(postgres ? "Seq Scan" : "TEMP B-TREE", StringComparison.Ordinal)),
                $"the actual scoped query uses {expected.Name} for an ordered range ({string.Join(" | ", plan)})");
            if (expected.Name == "kernel_playback_scope_rank")
            {
                Require(plan.Any(line => postgres
                        ? line.Contains("Index Cond:", StringComparison.Ordinal) &&
                          (line.Contains("\"Sequence\" = member.\"Sequence\"", StringComparison.Ordinal) ||
                           line.Contains("\"Activity\" = member.\"Activity\"", StringComparison.Ordinal))
                        : line.Contains("SEARCH p USING INDEX", StringComparison.Ordinal) &&
                          (line.Contains("Sequence=?", StringComparison.Ordinal) || line.Contains("Activity=?", StringComparison.Ordinal))),
                    "the bounded rank joins each scoped member to one exact observation");
            }
            Checked++;
            return result;
        }
    }

    private const string ReplacePlaybackProjector = """
        CREATE OR REPLACE FUNCTION public.kernel_project_root_playback_scopes() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            RETURN NULL;
        END
        $fn$;
        """;

    private static Task<string> ReadPlaybackProjectorAsync(KernelDb db, CancellationToken token) => db.Database.SqlQueryRaw<string>("""
        SELECT pg_catalog.pg_get_functiondef('public.kernel_project_root_playback_scopes()'::regprocedure) AS "Value"
        """).SingleAsync(token);

    private static async Task RefusePostgresPlaybackDriftAsync(Func<KernelDb> open, string run, ActorPlaybackQuery query, CancellationToken token)
    {
        await using var observer = open();
        await observer.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER kernel_project_root_playback_scopes ON "FoundationSequencedJournal";
            CREATE FUNCTION kernel_project_root_playback() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RETURN NULL;
            END
            $fn$;
            CREATE TRIGGER kernel_project_root_playback AFTER INSERT ON "FoundationSequencedJournal"
                FOR EACH ROW EXECUTE FUNCTION kernel_project_root_playback();
            """, token);
        try
        {
            await RefuseAsync("legacy projector");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER kernel_project_root_playback ON "FoundationSequencedJournal";
                DROP FUNCTION kernel_project_root_playback();
                CREATE TRIGGER kernel_project_root_playback_scopes AFTER INSERT ON "FoundationSequencedJournal"
                    FOR EACH ROW EXECUTE FUNCTION kernel_project_root_playback_scopes();
                """, token);
        }

        var installed = await ReadPlaybackProjectorAsync(observer, token);
        var bound = await ReadProjectorBindingAsync();
        await observer.Database.ExecuteSqlRawAsync(ReplacePlaybackProjector, token);
        try
        {
            Require(await ReadProjectorBindingAsync() == bound, "replacing the projector body preserves the installed trigger and function identities");
            await RefuseAsync("replacement body under the selected name");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync(installed, token);
        }

        await observer.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE TRIGGER kernel_project_root_playback_scopes AFTER INSERT ON "FoundationSequencedJournal"
                FOR EACH ROW WHEN (false) EXECUTE FUNCTION kernel_project_root_playback_scopes();
            """, token);
        try
        {
            await RefuseAsync("unexpected projector trigger predicate");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE TRIGGER kernel_project_root_playback_scopes AFTER INSERT ON "FoundationSequencedJournal"
                    FOR EACH ROW EXECUTE FUNCTION kernel_project_root_playback_scopes();
                """, token);
        }

        await observer.Database.ExecuteSqlRawAsync("""
            ALTER FUNCTION public.kernel_project_root_playback_scopes() SET search_path TO pg_catalog;
            """, token);
        try
        {
            await RefuseAsync("unexpected projector search path override");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync("""
                ALTER FUNCTION public.kernel_project_root_playback_scopes() RESET ALL;
                """, token);
        }

        await observer.Database.ExecuteSqlRawAsync("""
            DROP INDEX kernel_playback_scope_unknown;
            CREATE INDEX kernel_playback_scope_unknown ON "FoundationJournalPlaybackScopes"
                ("Partition", "Actor" COLLATE pg_catalog."default", "Sequence") WHERE "Known" = 0;
            """, token);
        try
        {
            await RefuseAsync("same-name scope index with the wrong Actor collation");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync("""
                DROP INDEX kernel_playback_scope_unknown;
                CREATE INDEX kernel_playback_scope_unknown ON "FoundationJournalPlaybackScopes"
                    ("Partition", "Actor", "Sequence") WHERE "Known" = 0;
                """, token);
        }

        await observer.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationSequencedJournal"
                DROP CONSTRAINT "FoundationSequencedJournal_Partition_Position_fkey";
            """, token);
        try
        {
            await RefuseAsync("missing cascade from raw Journal to sequenced history");
        }
        finally
        {
            await observer.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FoundationSequencedJournal"
                    ADD CONSTRAINT "FoundationSequencedJournal_Partition_Position_fkey"
                    FOREIGN KEY ("Partition", "Position") REFERENCES "Journal" ("Partition", "Position") ON DELETE CASCADE;
                """, token);
        }

        Task<string> ReadProjectorBindingAsync() => observer.Database.SqlQueryRaw<string>("""
            SELECT binding.oid::text || '/' || binding.tgfoid::text AS "Value"
            FROM pg_catalog.pg_trigger binding
            WHERE binding.tgrelid = 'public."FoundationSequencedJournal"'::regclass
                AND binding.tgname = 'kernel_project_root_playback_scopes'
            """).SingleAsync(token);

        async Task RefuseAsync(string drift)
        {
            var reads = new EfStore(open, postgres: true);
            try
            {
                await reads.ReadRootPlaybackAsync("default", run, query, token);
                throw new InvalidOperationException($"A frozen descendant window accepted the {drift}.");
            }
            catch (StoreFormatException)
            {
                Require(true, $"frozen descendant Playback rejects the {drift} with its marker and scope rows intact");
            }

            const string node = "playback-projector-drift";
            var actor = new PlaybackObservationProbe();
            var requests = PostgresPreviewHosting.CreateRequests(open, node, actor.Type);
            await using var owner = new PostgresPreviewHostedService(open, node, actor,
                message => throw new InvalidOperationException(message), requests: requests);
            try
            {
                await owner.StartAsync(token);
                throw new InvalidOperationException($"The owner allowed the {drift}.");
            }
            catch (StoreFormatException error) when (error.Message == "The installed playback projection is incomplete.")
            {
                Require(true, $"the direct PostgreSQL owner rejects the {drift} before starting a node");
            }
        }
    }

    private static DbContextOptions<KernelDb> PlaybackOptions(string connection, string? sqlite) =>
        sqlite is null
            ? new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = "public" }.ConnectionString).Options
            : new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
                { DataSource = sqlite, ForeignKeys = true, Pooling = false }.ConnectionString).Options;

    private static KernelDb OpenPlayback(DbContextOptions<KernelDb> options, bool postgres, SqliteAdmissionDefinitions definitions, bool descendants) =>
        postgres
            ? descendants ? PostgresPreviewHosting.CreatePlaybackDescendantsQualificationContext(options) : PostgresPreviewHosting.CreatePlaybackQualificationContext(options)
            : KernelDb.ForSqliteRootRuntime(options, definitions, descendants ? StoreFormat.RootPlaybackDescendantsQualification : StoreFormat.RootPlaybackQualification);

    private sealed record PlaybackActorKey(int Partition, string Actor);

    private static async Task RetirePlaybackAsync(string connection, string? sqlite, string run, CancellationToken token)
    {
        var postgres = sqlite is null;
        var options = PlaybackOptions(connection, sqlite);
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => OpenPlayback(options, postgres, definitions, descendants: true);
        var scope = EmbeddedTenant.Bind("default");
        await using var db = Open();
        var store = new EfStore(Open, postgres, fenceInBatch: true);
        var reads = postgres ? await store.OpenPreviewRootReadsAsync(cancellationToken: token)
            : new GeneratedRootReads(store, await SqliteRootAdmission.ReadSelectionCursorAsync(db, token), 100, "sqlite");
        var point = await reads.GetAsync(scope, new(run), token);
        var selected = point?.Selection ?? throw new InvalidOperationException("The completed playback run has no retirement selection.");
        var key = await db.Database.SqlQuery<PlaybackActorKey>($"""
            SELECT "Partition", "Actor" FROM "FoundationRootBirths"
            WHERE "Tenant" = {scope.Tenant} AND "Reference" = {run} AND "Creation" = 1
            """).SingleAsync(token);
        Require(await ScopesAsync() > 0, "the selected run has committed descendant membership before Delete");
        if (postgres)
        {
            var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
            Require(await admission.AdmitRootOperatorAsync(scope.Tenant, run, "playback-delete",
                ContractKinds.Delete, null, null, token, selected) is not null, "the selected PostgreSQL Delete is admitted");
        }
        else
        {
            var admission = new SqliteRootAdmission(Open, TimeSpan.FromHours(1), definitions);
            Require((await admission.OperateAsync(scope.Tenant, new(run), "playback-delete", ContractKinds.Delete,
                selected, null, null, token)).Outcome == AdmissionOutcome.Accepted, "the selected SQLite Delete is admitted");
        }
        while (await reads.GetAsync(scope, new(run), token) is not null)
            await Task.Delay(50, token);
        Require(await ScopesAsync() == 0, "fenced retirement cascades the exact run's Playback scope index");
        Task<long> ScopesAsync() => db.Database.SqlQuery<long>($"""
            SELECT CAST(COUNT(*) AS bigint) AS "Value" FROM "FoundationJournalPlaybackScopes"
            WHERE "Partition" = {key.Partition} AND "Actor" = {key.Actor}
            """).SingleAsync(token);
    }

    private sealed class PlaybackObservationProbe : Contract.IActorType
    {
        public string Type => "playback-observation-probe";
        public bool Withholds(ActorHeader header) => false;
        public long TerminalGrowth(ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(ActorHeader header, string state, long now) => false;
        public RestorePlan RestoreWaits(ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.StartSettings is not null)
            {
                turn.State = "ready";
                turn.Record(new { NativeStructure = 1, Kind = 2, At = turn.Now, Frame = "f1", Node = "probe" });
            }
            foreach (var message in turn.Inbox.Where(message => message.Kind == "record"))
            {
                using var observations = JsonDocument.Parse(message.Payload!);
                foreach (var observation in observations.RootElement.EnumerateArray()) turn.Record(observation);
            }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task RefusePlaybackNamespaceReplacementAsync(DbContextOptions<KernelDb> options, string run, bool descendants, CancellationToken token)
    {
        var definitions = new SqliteAdmissionDefinitions();
        await using var observer = OpenPlayback(options, postgres: true, definitions, descendants);
        await observer.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA playback_replacement;
            CREATE TABLE playback_replacement."KernelStoreVersions" AS TABLE public."KernelStoreVersions";
            CREATE TABLE playback_replacement."FoundationJournalPlayback" AS TABLE public."FoundationJournalPlayback";
            """, token);
        var swap = new PlaybackNamespaceSwap(async cancellationToken =>
            await observer.Database.ExecuteSqlRawAsync("""
                ALTER SCHEMA public RENAME TO playback_departed;
                ALTER SCHEMA playback_replacement RENAME TO public;
                """, cancellationToken));
        try
        {
            var intercepted = new DbContextOptionsBuilder<KernelDb>(options).AddInterceptors(swap).Options;
            var reads = new EfStore(() => OpenPlayback(intercepted, postgres: true, definitions, descendants), postgres: true);
            try
            {
                await reads.ReadRootPlaybackAsync("default", run, new(2, null, "f1", "f1"), token);
                throw new InvalidOperationException("The anchored playback accepted the replacement namespace.");
            }
            catch (StoreFormatException error) when (swap.Completed && error.Message == "The playback read changed its physical source.")
            {
                Require(true, "a compatible post-pin namespace replacement is refused by the exact anchor data statement");
            }
        }
        finally
        {
            if (swap.Completed)
                await observer.Database.ExecuteSqlRawAsync("""
                    ALTER SCHEMA public RENAME TO playback_replacement;
                    ALTER SCHEMA playback_departed RENAME TO public;
                    """, token);
            await observer.Database.ExecuteSqlRawAsync("DROP SCHEMA playback_replacement CASCADE", token);
        }
    }

    private sealed class PlaybackNamespaceSwap(Func<CancellationToken, Task> replace) : DbCommandInterceptor
    {
        internal bool Completed { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Completed && command.CommandText.Contains("/* playback exact anchor */", StringComparison.Ordinal))
            {
                await replace(cancellationToken);
                Completed = true;
            }
            return result;
        }
    }
}
