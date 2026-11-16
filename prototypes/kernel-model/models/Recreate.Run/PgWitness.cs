using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class PgWitness
{
    internal static async Task Run(string connection)
    {
    // The caller supplies an empty, experiment-owned database. Its lifecycle belongs to that caller.
    var workerMeter = Wire.Start("postgres", connection);
    var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(workerMeter.Rewrite(connection)).Options;
    var pgStore = new EfStore(() => new KernelDb(options), postgres: true);
    await pgStore.CreateAsync(1);
    var meter = Wire.Start("postgres", connection);
    await using var data = NpgsqlDataSource.Create(meter.Rewrite(connection));
    var pg = new PgKeyAdmission(data, 1);
    await pg.PrepareAsync();
    await PgBucketChecks.Run(data,pg);
    async Task WaitUntil(long until)
    {
        while (true)
        {
            long remaining = until - (await pgStore.PollAsync(0, 0, TimeSpan.Zero, 1)).Now;
            if (remaining <= 0) return;
            await Task.Delay(TimeSpan.FromMilliseconds(remaining));
        }
    }
    async Task Rollback(KeyRequest request)
    {
        // Force an actual unique violation between writing the new binding and the receipt.
        await pgStore.AddAsync(new(request.WireId, new("0/unrelated"), "collision"));
        // Ordinary namesakes coexist. Collision must use the exact trusted creator origin.
        await using var collision = data.CreateCommand("""
            INSERT INTO "Messages" ("InboxScope","Id","Partition","Target","Kind","Due","Seen","Attempts","Trial","Done")
            VALUES (9223372036854775807,$1,0,'0/unrelated','collision',0,0,0,0,false)
            """);
        collision.Parameters.AddWithValue(request.WireId);
        await collision.ExecuteNonQueryAsync();
        try { await pg.AcceptAsync(request); throw new InvalidOperationException("Expected atomic refusal."); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { }
        await using var verify = data.CreateCommand("SELECT (SELECT count(*) FROM key_binding WHERE key=$1) + (SELECT count(*) FROM key_receipt WHERE key=$1)");
        verify.Parameters.AddWithValue(request.EncodedKey);
        Check(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 0, "failed admission left a binding or receipt");
    }
    async Task LockedExpiry(KeyRequest request)
    {
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using var hold = await admin.BeginTransactionAsync();
        async Task<object?> SQL(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, admin, hold);
            cmd.Parameters.AddWithValue(request.EncodedKey);
            return await cmd.ExecuteScalarAsync();
        }
        await SQL("""SELECT pg_advisory_xact_lock(hashtextextended($1 COLLATE "C",4229))""");
        // Put the boundary ahead of the waiting command's transaction timestamp, independently of process speed.
        var pending = pg.AcceptAsync(request).AsTask();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await using var check = data.CreateCommand("""
                SELECT count(*) FROM pg_stat_activity WHERE wait_event = 'advisory' AND query LIKE 'SELECT * FROM keyed_accept%'
                """);
            if (Convert.ToInt64(await check.ExecuteScalarAsync()) > 0) break;
            if (pending.IsCompleted) throw new InvalidOperationException("Candidate did not wait on the key lock.");
            await Task.Delay(1);
        }
        var until = (long)(await SQL("""
            UPDATE key_binding SET keep_until=floor(extract(epoch FROM clock_timestamp())*1000)::bigint+50
            WHERE key=$1 RETURNING keep_until
            """))!;
        await WaitUntil(until);
        await hold.CommitAsync();
        var outcome = await pending;
        Check(outcome.Outcome == "accepted", "waiting request used a clock sample taken before its turn at the key lock");
        Console.WriteLine("PostgreSQL blocked rebind entered before expiry and accepted using store time after the key lock.");
    }
    await KeyChecks.Run("PostgreSQL", pgStore, pg, WaitUntil, exactClock: false, Rollback, () => meter.Trips, LockedExpiry);
    await KeyedEphemeral.Run("PostgreSQL", pgStore, pg, WaitUntil, () => workerMeter.Trips);
        await AdverseChecks.Run("PostgreSQL", pgStore, pg);
        async Task<int> StoredBytes(string actor)
        {
            await using var bytes=data.CreateCommand("""SELECT octet_length("State") FROM "Instances" WHERE "Id"=$1""");
            bytes.Parameters.AddWithValue(actor);return Convert.ToInt32(await bytes.ExecuteScalarAsync());
        }
        await KeyedMetadataChecks.Run("PostgreSQL",pgStore,pg,WaitUntil,()=>workerMeter.Trips,StoredBytes);
        var warm=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>await data.OpenConnectionAsync()));
        foreach(var connectionToReturn in warm)await connectionToReturn.DisposeAsync();
        await UnicodeKeys.Run("PostgreSQL",pgStore,pg,()=>meter.Executes,WaitUntil,()=>meter.Trips);
        await RetirementCost.Run(pgStore,data,() => workerMeter.Trips);
        await KeyStorageCost.Run(data,pg,()=>meter.Trips);
        Console.WriteLine($"PostgreSQL admission client calls={pg.Requests}, includes deliberate rollback.");
    }
    private static void Check(bool ok,string message) { if (!ok) throw new InvalidOperationException(message); }
}
