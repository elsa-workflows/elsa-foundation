using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.Native.Run;

internal static class UserTaskWaitFormatChecks
{
    private static readonly RootPublicEventHost EventHost = new("workflow", false);
    private static readonly StoreFormat Candidate = StoreFormat.RootUserTaskWaitsQualification;

    internal static async Task<int> RunAsync(string provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var shipped = StoreFormat.RootShipped;
        Require(Candidate == (shipped with { MessageProtocol = shipped.MessageProtocol + 1 }), "The task wait profile needs the next message protocol.");
        var capabilities = RootProfiles.UserTaskWaitsQualification;
        Require(capabilities == (RootProfiles.CapabilitiesOf(shipped) | RootCapabilities.UserTaskWaits)
            && RootProfiles.MissingPrerequisites(capabilities) == RootCapabilities.None, "The task wait profile must preserve the shipped capability closure.");

        if (provider == "sqlite")
            await SqliteAsync(token);
        else
            await PostgresAsync(token);

        Console.WriteLine($"{provider}: both task-wait format directions refused mismatches and preserved their store markers.");
        return 0;
    }

    private static async Task SqliteAsync(CancellationToken token)
    {
        var folder = Directory.CreateTempSubdirectory("actor-user-task-format");
        try
        {
            foreach (var selected in new StoreFormat?[] { null, Candidate })
            {
                var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(folder.FullName, selected is null ? "shipped.db" : "tasks.db"),
                    ForeignKeys = true,
                    Pooling = false
                }.ToString()).Options;
                var definitions = new SqliteAdmissionDefinitions();
                await using var current = KernelDb.ForSqliteRootShipped(options, selected, EventHost);
                await using var other = KernelDb.ForSqliteRootShipped(options, selected is null ? Candidate : null, EventHost);

                await EfStore.InstallSqliteShippedAsync(current, 1, token);
                await VerifyMarkersAsync(current, other, token);
                await RefusesOtherFormatAsync(() => EfStore.InstallSqliteShippedAsync(other, 1, token));
                await RefusesOtherFormatAsync(() => EfStore.RequireSqliteRootAdmissionAsync(other, definitions, token));
                await EfStore.RequireSqliteRootAdmissionAsync(current, definitions, token);
                await VerifyMarkersAsync(current, other, token);
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task PostgresAsync(CancellationToken token)
    {
        var shippedConnection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply an empty owned PostgreSQL database for the shipped profile.");
        var candidateConnection = Environment.GetEnvironmentVariable("KERNEL_USER_TASK_WAIT_POSTGRES")
            ?? throw new InvalidOperationException("Supply a second empty owned PostgreSQL database for the task wait profile.");

        foreach (var (connection, useCandidate) in new[] { (shippedConnection, false), (candidateConnection, true) })
        {
            var staged = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
            {
                Pooling = false
            }.ConnectionString).Options;
            KernelDb Open(bool candidate) => candidate
                ? KernelDb.ForPostgresUserTaskWaitsQualification(staged, EventHost)
                : KernelDb.ForPostgresRootShipped(staged, EventHost);
            await using var initial = Open(useCandidate);
            Require(await PostgresFixtureDatabase.IsEmptyAsync(initial, token), "Each PostgreSQL qualification database must be empty.");
            var store = new EfStore(() => Open(useCandidate), postgres: true);
            await store.CreateAsync(1);
            await store.InstallPreviewRootSchemaAsync(token);
            await using var current = Open(useCandidate);
            await using var other = Open(!useCandidate);

            await VerifyMarkersAsync(current, other, token);
            await RefusesOtherFormatAsync(() => EfStore.RequireStoreVersionsAsync(other, token));
            await EfStore.RequireStoreVersionsAsync(current, token);
            await VerifyMarkersAsync(current, other, token);
        }
    }

    private static async Task VerifyMarkersAsync(KernelDb current, KernelDb other, CancellationToken token)
    {
        var markers = await current.StoreVersions.AsNoTracking()
            .Where(marker => marker.Id == StoreVersionEntity.MarkerId || marker.Id == StoreDefinitionsRevision.MarkerId)
            .ToDictionaryAsync(marker => marker.Id, token);
        Require(markers[StoreVersionEntity.MarkerId].Format == current.RequiredFormat,
            "The installed marker must retain its selected message protocol.");
        var expected = current.RequiredFormat == Candidate ? RootProfiles.UserTaskWaitsQualification : RootProfiles.Shipped;
        Require(current.Capabilities == expected, "The physical profile must select its expected capability set.");
        var definitions = StoreDefinitionsRevision.Seed(current) ?? throw new InvalidOperationException("The physical definitions marker is required.");
        var otherDefinitions = StoreDefinitionsRevision.Seed(other) ?? throw new InvalidOperationException("Both profiles require physical definitions markers.");
        Require(markers[StoreDefinitionsRevision.MarkerId].Format == definitions.Format && definitions.Format == otherDefinitions.Format,
            "Only the wire protocol changes between these profiles.");
    }

    private static async Task RefusesOtherFormatAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            throw new InvalidOperationException("The physical provider accepted a different message protocol.");
        }
        catch (StoreFormatException)
        {
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
