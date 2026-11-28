using System.Text.Json;
using Kernel.Foundation.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Elsa.Orleans.ActorFoundation.Probe;

public sealed record PendingTimer(string Id, string Kind, string? Payload, long Due);

public sealed record Checkpoint(
    long Revision, ActorHeader Header, StartSpec Start, string? State, PendingTimer? Timer,
    IReadOnlyList<JournalEntry> Journal, string Silo);

public sealed class ProbeStore(NpgsqlDataSource database)
{
    public async Task InstallAsync(CancellationToken token)
    {
        foreach (var name in new[] { "PostgreSQL-Main.sql", "PostgreSQL-Clustering.sql" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "scripts", name);
            await using var script = database.CreateCommand(await File.ReadAllTextAsync(path, token));
            await script.ExecuteNonQueryAsync(token);
        }

        await using var create = database.CreateCommand("""
            CREATE TABLE actor_probe (
                id text PRIMARY KEY, tenant text NOT NULL, revision bigint NOT NULL,
                due bigint, checkpoint jsonb NOT NULL
            )
            """);
        await create.ExecuteNonQueryAsync(token);
    }

    public async Task<Checkpoint?> LoadAsync(ActorRef actor, string tenant, CancellationToken token)
    {
        await using var read = database.CreateCommand("SELECT checkpoint FROM actor_probe WHERE id = @id AND tenant = @tenant");
        read.Parameters.AddWithValue("id", actor.Id);
        read.Parameters.AddWithValue("tenant", tenant);
        return await read.ExecuteScalarAsync(token) is string value ? JsonSerializer.Deserialize<Checkpoint>(value) : null;
    }

    public async Task CommitAsync(Checkpoint checkpoint, long expectedRevision, CancellationToken token)
    {
        // State, journal, and the timer obligation become visible in the same conditional write.
        await using var save = database.CreateCommand(expectedRevision == 0
            ? """
              INSERT INTO actor_probe (id, tenant, revision, due, checkpoint)
              VALUES (@id, @tenant, @new_revision, @due, @checkpoint)
              ON CONFLICT (id) DO NOTHING
              """
            : """
              UPDATE actor_probe SET revision = @new_revision, due = @due, checkpoint = @checkpoint
              WHERE id = @id AND tenant = @tenant AND revision = @old_revision
              """);
        save.Parameters.AddWithValue("id", checkpoint.Header.Ref.Id);
        save.Parameters.AddWithValue("tenant", checkpoint.Header.Tenant);
        save.Parameters.AddWithValue("new_revision", checkpoint.Revision);
        if (expectedRevision != 0) save.Parameters.AddWithValue("old_revision", expectedRevision);
        save.Parameters.AddWithValue("due", NpgsqlDbType.Bigint, (object?)checkpoint.Timer?.Due ?? DBNull.Value);
        save.Parameters.AddWithValue("checkpoint", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(checkpoint));
        if (await save.ExecuteNonQueryAsync(token) != 1)
            throw new InvalidOperationException("The committed actor revision changed. Retry from the committed checkpoint.");
    }

    public async Task<long> ReadClockAsync(CancellationToken token)
    {
        await using var read = database.CreateCommand("SELECT floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint");
        return (long)(await read.ExecuteScalarAsync(token))!;
    }

    public async Task<IReadOnlyList<(ActorRef Actor, string Tenant, string TimerId)>> DueAsync(CancellationToken token)
    {
        await using var read = database.CreateCommand("""
            SELECT id, tenant, checkpoint -> 'Timer' ->> 'Id' FROM actor_probe
            WHERE due <= floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint
            ORDER BY due, id LIMIT 16
            """);
        await using var rows = await read.ExecuteReaderAsync(token);
        List<(ActorRef, string, string)> due = [];
        while (await rows.ReadAsync(token))
            due.Add((new(rows.GetString(0)), rows.GetString(1), rows.GetString(2)));
        return due;
    }
}
