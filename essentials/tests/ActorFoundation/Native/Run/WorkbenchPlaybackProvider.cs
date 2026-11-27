using System.Data.Common;
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

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    private static async Task VerifyPlaybackProviderAsync(string connection, string? sqlite, string run, CancellationToken token)
    {
        var postgres = sqlite is null;
        var options = new DbContextOptionsBuilder<KernelDb>();
        if (postgres) options.UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = "public" }.ConnectionString);
        else options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = sqlite, ForeignKeys = true }.ConnectionString);
        KernelDb Open() => postgres ? PostgresPreviewHosting.CreatePlaybackQualificationContext(options.Options)
            : KernelDb.ForSqliteRootRuntime(options.Options, new SqliteAdmissionDefinitions(), StoreFormat.RootPlaybackQualification);
        var reads = new EfStore(Open, postgres);
        var query = new ActorPlaybackQuery(2, 0, null, null);
        var first = await reads.ReadRootPlaybackAsync("default", run, query, token);
        Require(first is { Availability: ActorPlaybackAvailability.Available, Count: > 0 }, "the retained provider has indexed authored activity");
        if (postgres)
            await RefusePlaybackNamespaceReplacementAsync(options.Options, run, token);

        const string node = "playback-observation-probe";
        var actor = new PlaybackObservationProbe();
        var sqliteHost = sqlite is null ? null : SqliteEmbeddedFoundation.ForPlaybackQualification(sqlite, node, actor.Type);
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
        string[] malformed =
        [
            """{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f900","Node":["bad"]}""",
            """{"NativeStructure":1,"Kind":2,"At":1e3,"Frame":"f901","Node":"bad"}""",
            """{"NativeStructure":1,"Kind":2,"At":253402300800000,"Frame":"f902","Node":"bad"}""",
            """{"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f903","Node":"bad"}""",
            """{"NativeStructure":1,"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f904","Node":"bad"}"""
        ];
        var stale = prefix;
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
            query with { UpperSequence = prefix.UpperSequence, RetainedAfterSequence = prefix.RetainedAfterSequence }, token);
        Require(stale is { Availability: ActorPlaybackAvailability.Unavailable, Reason: "occurrence-observation-unknown", Items.Count: 0 } &&
            frozen is { Availability: ActorPlaybackAvailability.Available } && frozen.Count == prefix.Count && frozen.LatestSequence > frozen.UpperSequence,
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

    private static async Task RefusePlaybackNamespaceReplacementAsync(DbContextOptions<KernelDb> options, string run, CancellationToken token)
    {
        await using var observer = PostgresPreviewHosting.CreatePlaybackQualificationContext(options);
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
            var reads = new EfStore(() => PostgresPreviewHosting.CreatePlaybackQualificationContext(intercepted), postgres: true);
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
