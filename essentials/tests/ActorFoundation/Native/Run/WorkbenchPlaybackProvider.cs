using System.Data.Common;
using Microsoft.Data.Sqlite;
using System.Text.Json;
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
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => OpenPlayback(options, postgres, definitions, descendants);
        var reads = new EfStore(Open, postgres);
        var query = new ActorPlaybackQuery(2, 0, null, null);
        var first = await reads.ReadRootPlaybackAsync("default", run, query, token);
        Require(first is { Availability: ActorPlaybackAvailability.Available, Count: > 0 }, "the retained provider has indexed authored activity");
        if (descendants)
        {
            await using var plans = Open();
            await RequirePlaybackScopeIndexesAsync(plans, postgres, token);
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
            Require(original is { Availability: ActorPlaybackAvailability.Available, Count: 1 } &&
                frozenScope is { Availability: ActorPlaybackAvailability.Available, Count: 1 } &&
                clean is { Availability: ActorPlaybackAvailability.Available, Count: 3 } &&
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

        static void Escalate(string message) => throw new InvalidOperationException(message);
    }

    private static async Task RequirePlaybackScopeIndexesAsync(KernelDb db, bool postgres, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        const string actor = """
            FROM "FoundationJournalPlaybackScopes" WHERE "Partition" = 1 AND "Actor" = 'qualification'
            """;
        (string Sql, string Name, string SqliteKey)[] seeks =
        [
            ($"""
                SELECT "Ordinal" {actor} AND "Scope" = 'f1' AND "Ordinal" BETWEEN 1 AND 3 ORDER BY "Ordinal" LIMIT 4
                """, "kernel_playback_scope_rank", "Ordinal>? AND Ordinal<?"),
            ($"""
                SELECT "Ordinal" {actor} AND "Scope" = 'f1' AND "Sequence" BETWEEN 1 AND 9 ORDER BY "Sequence" DESC LIMIT 1
                """, "kernel_playback_scope_sequence", "Sequence>? AND Sequence<?"),
            ($"""
                SELECT "Sequence" {actor} AND "Known" = 0 AND "Sequence" BETWEEN 1 AND 9 ORDER BY "Sequence" LIMIT 1
                """, "kernel_playback_scope_unknown", "kernel_playback_scope_unknown")
        ];
        foreach (var seek in seeks)
        {
            command.CommandText = (postgres ? "EXPLAIN (COSTS OFF) " : "EXPLAIN QUERY PLAN ") + seek.Sql;
            await using var reader = await command.ExecuteReaderAsync(token);
            var seekIndex = false;
            while (await reader.ReadAsync(token))
            {
                var line = reader.GetString(postgres ? 0 : 3);
                seekIndex |= line.Contains(postgres ? seek.Name : seek.SqliteKey, StringComparison.Ordinal);
                Require(!line.Contains(postgres ? "Seq Scan" : "TEMP B-TREE", StringComparison.Ordinal),
                    $"{seek.Name} uses an ordered range without scanning or sorting the scoped relation");
            }
            Require(seekIndex, $"{seek.Name} is selected for its bounded lookup");
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
