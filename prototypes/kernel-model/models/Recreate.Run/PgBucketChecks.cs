using Kernel.Durable;
using Npgsql;

internal static class PgBucketChecks
{
    internal static async Task Run(NpgsqlDataSource data,IKeyAdmission ingress)
    {
        var request=new KeyRequest("bucket-"+Guid.NewGuid().ToString("N"),"create",Kinds.Start,Start:new("probe","child",Ephemeral:true),KeepForMillis:1);
        var other=request with{Key=request.Key+"-other"};
        await using var insert=data.CreateCommand("""
            WITH binding_collision AS (INSERT INTO key_binding VALUES(md5($1),$2,'g0/key/99999999',9223372036854775807) RETURNING 1),
            receipt_key_collision AS (INSERT INTO key_receipt VALUES(md5($1),md5($3),$2,$3,$4,'g0/key/99999999',9223372036854775807) RETURNING 1)
            INSERT INTO key_receipt VALUES(md5($1),md5($3),$1,$5,$4,'g0/key/99999998',9223372036854775807)
            """);
        insert.Parameters.AddWithValue(request.EncodedKey);insert.Parameters.AddWithValue(other.EncodedKey);
        insert.Parameters.AddWithValue(request.EncodedOperation);insert.Parameters.AddWithValue(request.Digest);insert.Parameters.AddWithValue((request with{Operation="different"}).EncodedOperation);await insert.ExecuteNonQueryAsync();
        var accepted=await ingress.AcceptAsync(request);
        Harness.Check(accepted.Outcome=="accepted" && accepted.RunId is not ("g0/key/99999999" or "g0/key/99999998"),"shared bucket aliased another full key");
        Harness.Check(await ingress.AcceptAsync(request)==accepted && (await ingress.AcceptAsync(request with{Payload="different"})).Outcome=="conflict","bucket replay/conflict changed identity");
        await using var check=data.CreateCommand("SELECT (SELECT count(*) FROM key_binding WHERE key_hash=md5($1))+(SELECT count(*) FROM key_receipt WHERE key_hash=md5($1) AND operation_hash=md5($2))");
        check.Parameters.AddWithValue(request.EncodedKey);check.Parameters.AddWithValue(request.EncodedOperation);
        Harness.Check(Convert.ToInt64(await check.ExecuteScalarAsync())==5,"shared bucket rows overwritten");
        Console.WriteLine("PostgreSQL: deliberately shared binding/receipt hash buckets kept full key identity, replay and conflict distinct; constructed bucket rows, not a claimed cryptographic collision.");
    }
}
