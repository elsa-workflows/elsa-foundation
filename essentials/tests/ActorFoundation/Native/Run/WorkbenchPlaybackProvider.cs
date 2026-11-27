using System.Data.Common;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

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

        await using var write = Open();
        var root = await write.Database.SqlQuery<PlaybackFixtureRoot>($"""
            SELECT "Actor" AS "Id", "Partition" FROM "FoundationRootBirths"
            WHERE "Tenant" = 'default' AND "Reference" = {run} AND "Creation" = 1
            """).SingleAsync(token);
        var position = await write.Journal.Where(row => row.Partition == root.Partition).MaxAsync(row => row.Position, token);
        var revision = await write.Journal.Where(row => row.Partition == root.Partition && row.Instance == root.Id).MaxAsync(row => row.Revision, token);
        Task AppendAsync(string entries) => write.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
            VALUES ({root.Partition}, {++position}, {root.Id}, {++revision}, {entries})
            """, token);

        await AppendAsync("""["b:f900:unmarked","{\"NativeStructure\":1,\"Kind\":2,\"At\":1,\"Frame\":\"f900\",\"Node\":\"lookalike\"}"]""");
        var prefix = await reads.ReadRootPlaybackAsync("default", run, query, token);
        Require(prefix is { Availability: ActorPlaybackAvailability.Available } && prefix.Count == first!.Count,
            "unmarked diagnostic strings leave Playback membership unchanged");
        string[] malformed =
        [
            """{"NativeStructure":1,"Kind":2,"At":1000,"Frame":"f900","Node":["bad"]}""",
            """{"NativeStructure":1,"Kind":2,"At":1e3,"Frame":"f901","Node":"bad"}""",
            """{"NativeStructure":1,"Kind":2,"At":253402300800000,"Frame":"f902","Node":"bad"}""",
            """{"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f903","Node":"bad"}""",
            """{"NativeStructure":1,"NativeStructure":2,"Kind":2,"At":1000,"Frame":"f904","Node":"bad"}"""
        ];
        foreach (var entry in malformed) await AppendAsync("[" + entry + "]");
        var invalid = await write.Database.SqlQuery<long>($"""
            SELECT CAST(COUNT(*) AS bigint) AS "Value" FROM "FoundationJournalPlaybackInvalid"
            WHERE "Partition" = {root.Partition} AND "Actor" = {root.Id}
            """).SingleAsync(token);
        Require(invalid == malformed.Length, "every malformed marked observation gets a retained refusal receipt");
        var stale = await reads.ReadRootPlaybackAsync("default", run, query, token);
        var frozen = await reads.ReadRootPlaybackAsync("default", run,
            query with { UpperSequence = prefix!.UpperSequence, RetainedAfterSequence = prefix.RetainedAfterSequence }, token);
        Require(stale is { Availability: ActorPlaybackAvailability.Unavailable, Reason: "occurrence-observation-unknown", Items.Count: 0 } &&
            frozen is { Availability: ActorPlaybackAvailability.Available } && frozen.Count == prefix.Count && frozen.LatestSequence > frozen.UpperSequence,
            "malformed marked observations refuse fresh admission while earlier frozen membership remains playable");
    }

    private sealed record PlaybackFixtureRoot(string Id, int Partition);

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
