using Kernel.Durable;
using Npgsql;

internal static class KeyStorageCost
{
    internal static async Task Run(NpgsqlDataSource data,IKeyAdmission ingress,Func<long> edges)
    {
        var random=new Random(4229);
        string Wide()=>new(Enumerable.Range(0,4096).Select(_=>(char)random.Next(65536)).ToArray());
        foreach(var (profile,key,operation) in new[]{("ascii","key","create"),("NUL-surrogate","key\0\uD800","create\0\uDFFF"),("wide-4096",Wide(),Wide())})
        {
            var request=new KeyRequest(Guid.NewGuid().ToString("N")+key,operation,Kinds.Start,Start:new("probe","child",Ephemeral:true),KeepForMillis:1);
            await using var begin=data.CreateCommand("SELECT pg_current_wal_insert_lsn()::text");
            var lsn=(string)(await begin.ExecuteScalarAsync())!;var before=edges();
            Harness.Check((await ingress.AcceptAsync(request)).Outcome=="accepted","key storage sample admission");
            var trips=edges()-before;
            await using var measure=data.CreateCommand("""
                SELECT pg_wal_lsn_diff(pg_current_wal_insert_lsn(),$1::pg_lsn),pg_column_size(b),pg_column_size(r),
                  octet_length(b.key),octet_length(r.operation),pg_column_size(b.key),pg_column_size(r.operation),
                  (SELECT pg_relation_size('key_binding')),(SELECT pg_indexes_size('key_binding')),
                  (SELECT pg_relation_size('key_receipt')),(SELECT pg_indexes_size('key_receipt'))
                FROM key_binding b JOIN key_receipt r ON r.key=b.key WHERE b.key=$2 AND r.operation=$3
                """);
            measure.Parameters.AddWithValue(lsn);measure.Parameters.AddWithValue(request.EncodedKey);measure.Parameters.AddWithValue(request.EncodedOperation);
            await using var reader=await measure.ExecuteReaderAsync();Harness.Check(await reader.ReadAsync(),"key storage sample rows");
            Console.WriteLine($"PostgreSQL key storage {profile}: admission TCP edges={trips}; WAL bytes={reader.GetDecimal(0)}; pg_column_size(binding/receipt)={reader.GetInt32(1)}/{reader.GetInt32(2)}; encoded key/operation octets={reader.GetInt32(3)}/{reader.GetInt32(4)}; pg_column_size(key/operation)={reader.GetInt32(5)}/{reader.GetInt32(6)}; cumulative heap/index allocation binding={reader.GetInt64(7)}/{reader.GetInt64(8)}, receipt={reader.GetInt64(9)}/{reader.GetInt64(10)}. Datum sizes and page allocations are separate; no retention/storage budget selected.");
        }
    }
}
