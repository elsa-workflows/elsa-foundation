using Kernel.Durable;
using Kernel.Runtime;
using Npgsql;

internal static class RetirementCost
{
    internal static async Task Run(IKernelStore store,NpgsqlDataSource data,Func<long> trips)
    {
        var lease=(await store.TakeAsync(0,"retirement-cost",TimeSpan.FromMinutes(1)))!;
        const string raw="g0/child/1.9000000.1", kept="g0/child/1.9000000.2";
        InstanceRow Row(string id)=>new(id,"probe","child",InstanceStatus.Finished,"null",1,null);
        TurnCommit Create(string id)=>new(id,Row(id),null,[],[],[],[],[],"[]");
        var batch=new CommitBatch(0,lease.Epoch,lease.Position,TimeSpan.FromMinutes(1),false,[Create(raw),Create(kept)],[],[]);
        Harness.Check((await store.CommitAsync(batch)).Landed,"cost setup");
        long position=lease.Position+2;
        async Task<(long Trips,decimal Wal)> Sample(string id,bool marker)
        {
            await using var begin=data.CreateCommand("SELECT pg_current_wal_insert_lsn()::text");
            var lsn=(string)(await begin.ExecuteScalarAsync())!;
            var before=trips();
            var turn=new TurnCommit(id,marker?CreationProtocol.Retire(Row(id)):null,1,[],[],[],[],[],"[\"deleted\"]",Unsubscribe:true,Delete:true);
            Harness.Check((await store.CommitAsync(batch with {Position=position,Turns=[turn]})).Landed,"cost retirement");
            var count=trips()-before;position++;
            await using var end=data.CreateCommand("SELECT pg_wal_lsn_diff(pg_current_wal_insert_lsn(),$1::pg_lsn)");
            end.Parameters.AddWithValue(lsn);
            return(count,(decimal)(await end.ExecuteScalarAsync())!);
        }
        var control=await Sample(raw,false);var retirement=await Sample(kept,true);
        await using var size=data.CreateCommand("""
            SELECT pg_column_size(i),pg_column_size(i."Retired") FROM "Instances" i WHERE i."Id"=$1
            """);size.Parameters.AddWithValue(kept);
        await using var reader=await size.ExecuteReaderAsync();Harness.Check(await reader.ReadAsync(),"marker size row");
        Console.WriteLine($"PostgreSQL physical retirement sample: raw delete trips={control.Trips}, WAL bytes={control.Wal}; marker trips={retirement.Trips}, WAL bytes={retirement.Wal}; retained tuple bytes={reader.GetInt32(0)}, bool datum bytes={reader.GetInt32(1)}.");
        Harness.Check(control.Trips==retirement.Trips,"physical retirement added a wire round trip");
        await store.ReleaseAsync(0,lease.Epoch);
    }
}
