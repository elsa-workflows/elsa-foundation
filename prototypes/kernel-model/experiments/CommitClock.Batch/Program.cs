using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Npgsql;

static class Probe
{
    const string Now = "(extract(epoch FROM clock_timestamp())*1000000)::bigint";
    const long Retention = 1_250_000;
    static int port;
    const string FixtureVersion = "clock-seal-batch-r2";
    static string database = "", password = "";
    static FileStream? evidence;
    static readonly ConcurrentBag<object> calls = [];
    static readonly List<object> observations = [];
    static void Save(string status)
    {
        if(evidence is null)return;
        byte[] bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{status,observations,calls},new JsonSerializerOptions{WriteIndented=true})+"\n");
        evidence.Position=0;evidence.Write(bytes);evidence.SetLength(bytes.Length);evidence.Flush(true);
    }
    static readonly string SealSql = $$"""
        WITH fence AS (
          UPDATE partition SET lease_until={{Now}}+10000000
          WHERE id=$1 AND epoch=$2 RETURNING id
        ), visible AS MATERIALIZED (
          SELECT o.occurrence FROM obligation o
          JOIN fence f ON f.id=o.partition
          JOIN actor a ON a.occurrence=o.occurrence AND a.partition=o.partition
          WHERE o.phase='pending' AND a.state='ended' AND a.revision>=o.original_revision
          ORDER BY o.occurrence LIMIT $3 FOR UPDATE OF o SKIP LOCKED
        ), sealed AS (
          UPDATE obligation o SET phase='sealed',anchor_us={{Now}}
          FROM visible v WHERE o.occurrence=v.occurrence AND o.phase='pending'
          RETURNING o.anchor_us
        ) SELECT count(*)::text FROM sealed
        """;
    // One data-modifying CTE statement, one PostgreSQL implicit write transaction.
    static string Batched(string sql) => sql;
    static void Require(bool condition,string text)
    { if(!condition)throw new Exception(text);Console.WriteLine("PASS "+text); }

    sealed class Session(Wire wire,NpgsqlConnection connection) : IAsyncDisposable
    {
        readonly int pid=connection.ProcessID;
        public int Pid => pid;
        public long Trips => wire.Trips;
        public string Trace => wire.Trace;
        public static async Task<Session> Open(string? selectedDatabase=null)
        {
            var relay=new Wire(port);
            var cs=new NpgsqlConnectionStringBuilder { Host="127.0.0.1",Port=relay.Port,
                Database=selectedDatabase??database,Username="postgres",Password=password,
                SslMode=SslMode.Disable,Pooling=false,Timeout=10,CommandTimeout=30,
                ApplicationName="kernel4229_seal_batch_fixture" };
            var db=new NpgsqlConnection(cs.ConnectionString);await db.OpenAsync();
            Volatile.Write(ref relay.Armed,true);return new(relay,db);
        }
        public async Task<string?> Sql(string stage,string sql,params object[] args)
        {
            var start=wire.Trips;await using var command=new NpgsqlCommand(sql,connection);
            foreach(var value in args)command.Parameters.Add(new NpgsqlParameter {Value=value});
            try {var value=await command.ExecuteScalarAsync();return value is null or DBNull?null:Convert.ToString(value,CultureInfo.InvariantCulture);}
            finally {calls.Add(new{stage,pid=Pid,driverSubmissions=1,wireExchanges=wire.Trips-start});}
        }
        public async Task Lose(string stage,string sql,Func<Task> observer,params object[] args)
        {
            wire.ArmHold();var pending=Sql(stage,sql,args);
            await wire.Held.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Require(!pending.IsCompleted,stage+" caller has no answer while fresh upstream idle ReadyForQuery is held");
            Console.WriteLine("WIRE "+stage+" "+wire.Trace);await observer();wire.Release.TrySetResult(false);
            try {await pending;throw new Exception(stage+" unexpectedly received success");}
            catch(NpgsqlException) {Console.WriteLine("LOST "+stage+" actual connection closed after verified server commit");}
        }
        public async ValueTask DisposeAsync(){await connection.DisposeAsync();wire.Dispose();}
    }
    static async Task<long> Clock(Session s,string stage) => long.Parse((await s.Sql(stage,"SELECT "+Now))!);
    static async Task<long> Scalar(Session s,string stage,string sql,params object[] args) => long.Parse((await s.Sql(stage,sql,args))!);
    static Task<long> Pending(Session s,int id) => Scalar(s,"observer","SELECT count(*) FROM obligation WHERE partition=$1 AND phase='pending'",id);
    static Task<long> Sealed(Session s,int id) => Scalar(s,"observer","SELECT count(*) FROM obligation WHERE partition=$1 AND phase='sealed'",id);
    static async Task<string> Digest(Session s,int id) => (await s.Sql("observer","SELECT string_agg(occurrence||':'||anchor_us::text,',' ORDER BY occurrence) FROM obligation WHERE partition=$1",id))!;
    static async Task Business(Session s,Session observer,int id,int count,bool lose=false)
    {
        await s.Sql("business","BEGIN ISOLATION LEVEL READ COMMITTED");
        await s.Sql("business",$$"""
          WITH f AS (UPDATE partition SET lease_until={{Now}}+10000000 WHERE id=$1 AND epoch=1 RETURNING id),
          a AS (INSERT INTO actor SELECT $1,'run-'||$1::text||'-'||g,1,'ended'
              FROM generate_series(1,$2) g CROSS JOIN f RETURNING partition,occurrence),
          e AS (INSERT INTO effect SELECT occurrence,1 FROM a RETURNING occurrence)
          INSERT INTO obligation SELECT partition,occurrence,1,pg_current_xact_id(),$3,'pending',NULL FROM a
          """,id,count,Retention);
        Require(await Pending(observer,id)==0,"business "+id+" pending rows are invisible before commit");
        long lower=await Clock(observer,"observer");
        if(lose)await s.Lose("lost-business-commit","COMMIT",async()=>{
            Require(await Pending(observer,id)==count,"independent primary observes committed pending rows before caller receives any reply");
        });else await s.Sql("business","COMMIT");
        Require(await Pending(observer,id)==count,"business "+id+" pending rows committed");
        observations.Add(new{kind="business",id,count,beforeCommitLowerUs=lower,firstVisibleUpperUs=await Clock(observer,"observer")});
    }
    static async Task<long> Seal(Session s,int id,int epoch,int limit,string stage,bool batch=true)
        => await Scalar(s,stage,batch?Batched(SealSql):SealSql,id,epoch,limit);
    static Task<long> Wal(Session s) => Scalar(s,"observer","SELECT pg_wal_lsn_diff(pg_current_wal_insert_lsn(),'0/0')::bigint");
    static async Task WaitBlocked(Session observer,int waiter,int holder)
    {
        for(int i=0;i<150;i++){
            if(await observer.Sql("observer","SELECT $1=ANY(pg_blocking_pids($2))",holder,waiter)=="True"){
                Require(true,$"physical PostgreSQL waiter {waiter} is blocked by holder {holder}");return;
            }
            await Task.Delay(20);
        }
        throw new Exception("expected PostgreSQL row-lock wait not observed");
    }
    static async Task EarlyOnly()
    {
        await using var observer=await Session.Open();
        Require(await Scalar(observer,"setup","SELECT count(*) FROM fixture_run WHERE version=$1 AND completed AND database_oid=(SELECT oid FROM pg_database WHERE datname=current_database())",FixtureVersion)==1,"matching full fixture completed successfully");
        Require(await Scalar(observer,"setup","SELECT count(*) FROM obligation WHERE partition IN (9,10)")==0,"clock-control occurrences absent; existing data retained");
        Require(await Scalar(observer,"setup","SELECT count(*) FROM partition WHERE id=10")==0,"additional clock-control partition absent");
        await observer.Sql("setup","INSERT INTO partition(id,epoch) VALUES(10,1)");
        await using var oldTransaction=await Session.Open();
        await oldTransaction.Sql("clock-control","BEGIN ISOLATION LEVEL READ COMMITTED");
        long early=await Scalar(oldTransaction,"clock-control","SELECT (extract(epoch FROM transaction_timestamp())*1000000)::bigint");
        long beforeBusiness;
        while((beforeBusiness=await Clock(observer,"clock-control"))<=early+Retention)await Task.Delay(20);
        await using(var writer=await Session.Open())await Business(writer,observer,9,1);
        await using(var writer=await Session.Open())await Business(writer,observer,10,1);
        long committedVisible=await Clock(observer,"clock-control");
        Require(await Scalar(oldTransaction,"early-clock-mutant",SealSql.Replace("clock_timestamp()","transaction_timestamp()"),9,1,1)==1,"old-transaction-time mutant seals its separate occurrence");
        Require(await Scalar(oldTransaction,"late-clock-positive",SealSql,10,1,1)==1,"clock_timestamp positive seals its separate occurrence in same old transaction");
        await oldTransaction.Sql("clock-control","COMMIT");
        long bad=await Scalar(observer,"clock-control","SELECT anchor_us FROM obligation WHERE partition=9");
        long good=await Scalar(observer,"clock-control","SELECT anchor_us FROM obligation WHERE partition=10");
        Require(bad==early && bad+Retention<beforeBusiness,"mutant expiry precedes clock observed BEFORE either business transaction began");
        Require(good>=committedVisible,"positive anchor follows clock observed AFTER both business commits");
        observations.Add(new{kind="clock-lower-bound-pair",earlyTransactionUs=early,mutantAnchorUs=bad,mutantDeadlineUs=bad+Retention,beforeEitherBusinessUs=beforeBusiness,afterBothCommittedVisibleUs=committedVisible,positiveAnchorUs=good});
        Save("PASS");Console.WriteLine("DONE clock lower-bound counterfactual only; prior full run retained separately");
    }
    static async Task<int> Main(string[] args)
    {
        try {
            if(args.Length is not (4 or 5) || (args.Length==5 && args[4]!="--early-clock-control"))
                throw new ArgumentException("Expected port, database, password file, output directory and optional --early-clock-control only");
            if(!int.TryParse(args[0],out port) || port is <1 or >65535)throw new ArgumentException("Invalid loopback port");
            database=args[1];
            Require(System.Text.RegularExpressions.Regex.IsMatch(database,@"\Aksb_[a-f0-9]{8}\z"),"explicit fresh database name validated");
            evidence=new FileStream(Path.Combine(args[3],"observations.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            Save("IN_PROGRESS");password=File.ReadAllText(args[2]).Trim();
            if(args.Length==5)await EarlyOnly();else await Full();
            return 0;
        }
        catch(Exception e){Save("FAILED");Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
        finally{evidence?.Dispose();}
    }
    static async Task Full()
    {
        await using(var admin=await Session.Open("postgres")){
            Require(await Scalar(admin,"setup","SELECT count(*) FROM pg_database WHERE datname=$1",database)==0,"target database absent; no cleanup or DROP");
            await admin.Sql("setup","CREATE DATABASE "+database);
        }
        await using var observer=await Session.Open();
        await observer.Sql("setup",await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"schema.sql")));
        await observer.Sql("setup","INSERT INTO fixture_run VALUES($1,(SELECT oid FROM pg_database WHERE datname=current_database()),false)",FixtureVersion);
        var settings=await observer.Sql("setup","SELECT current_setting('server_version')||'|'||current_setting('fsync')||'|'||current_setting('synchronous_commit')||'|'||current_setting('full_page_writes')||'|'||current_setting('track_commit_timestamp')");
        Console.WriteLine("SETTINGS "+settings);Require(settings!.Contains("|on|on|on|off"),"durability settings on; native commit timestamps off");
        await observer.Sql("setup","INSERT INTO partition(id,epoch) SELECT g,1 FROM generate_series(1,9) g");

        foreach(var pair in new[]{(Id:1,Count:1),(Id:2,Count:16),(Id:3,Count:1000)}){
            await using(var writer=await Session.Open())await Business(writer,observer,pair.Id,pair.Count);
            long visible=await Clock(observer,"observer"),before=await Wal(observer);
            await using var sealer=await Session.Open();long trips=sealer.Trips;
            Require(await Seal(sealer,pair.Id,1,pair.Count,"normal-seal-"+pair.Count)==pair.Count,"bounded batch seals "+pair.Count+" committed obligations");
            long wire=sealer.Trips-trips,after=await Wal(observer);
            Require(await Scalar(observer,"observer","SELECT min(anchor_us) FROM obligation WHERE partition=$1",pair.Id)>=visible,"all anchors sampled after independently verified business visibility");
            Require(await Pending(observer,pair.Id)==0,"batch pending count drained");
            observations.Add(new{kind="normal-batch",id=pair.Id,obligations=pair.Count,driverSubmissions=1,sqlStatements=1,writeTransactions=1,wireExchanges=wire,walBytes=after-before,trace=sealer.Trace});
            Save("IN_PROGRESS");
        }

        await using(var writer=await Session.Open())await Business(writer,observer,4,17);
        await using(var sealer=await Session.Open()){
            foreach(var n in new[]{5,5,5,2})Require(await Seal(sealer,4,1,5,"bounded-five")==n,"batch limit5 seals exact remaining count "+n);
            Require(await Pending(observer,4)==0,"bounded passes eventually drain17 in four rounds");
        }

        await using(var writer=await Session.Open())await Business(writer,observer,5,16,lose:true);
        Require(await Scalar(observer,"take","UPDATE partition SET epoch=2 WHERE id=5 AND epoch=1 RETURNING epoch")==2,"fresh take fences lost business caller");
        await using(var stale=await Session.Open())Require(await Seal(stale,5,1,16,"stale-seal")==0,"stale epoch cannot seal any marker");
        long lossVisible=await Clock(observer,"observer");string lossDigest="";
        await using(var sealer=await Session.Open()){
            long start=sealer.Trips;
            await sealer.Lose("lost-batched-seal",Batched(SealSql),async()=>{
                Require(await Sealed(observer,5)==16,"independent primary sees entire seal while caller has no answer");
                Require(await Scalar(observer,"observer","SELECT min(anchor_us) FROM obligation WHERE partition=5")>=lossVisible,"lost seal anchor follows committed business visibility");
                lossDigest=await Digest(observer,5);
            },5,2,16);
            observations.Add(new{kind="lost-batched-seal",driverSubmissions=1,sqlStatements=1,writeTransactions=1,wireExchanges=sealer.Trips-start,obligations=16});
        }
        await observer.Sql("take","UPDATE partition SET epoch=3 WHERE id=5 AND epoch=2");
        await using(var successor=await Session.Open()){
            Require(await Digest(successor,5)==lossDigest,"cold successor reads exact immutable anchors after second lost answer");
            Require(await Seal(successor,5,3,16,"duplicate-seal")==0,"reprocessing does not move sealed anchors");
            Require(await Digest(observer,5)==lossDigest,"duplicate leaves original anchors unchanged");
        }
        Require(await Scalar(observer,"observer","SELECT count(*) FROM effect WHERE occurrence LIKE 'run-5-%' AND body_count=1")==16,"business effects committed once; no replayed business body");

        await using(var writer=await Session.Open())await Business(writer,observer,6,16);
        await using(var first=await Session.Open())await using(var second=await Session.Open()){
            await first.Sql("race-first","BEGIN");Require(await Seal(first,6,1,16,"race-first",batch:false)==16,"first resolver stages16 seals under partition row lock");
            var loser=Seal(second,6,1,16,"race-second");await WaitBlocked(observer,second.Pid,first.Pid);
            await first.Lose("race-first-lost-commit","COMMIT",async()=>Require(await Sealed(observer,6)==16,"race winner commits before lost reply"));
            Require(await loser==0,"blocked competitor sees committed seals and does not resample");
            string digest=await Digest(observer,6);await observer.Sql("actor-revision","UPDATE actor SET revision=2 WHERE partition=6");
            Require(await Digest(observer,6)==digest,"later actor revisions do not move original seals");
        }

        await using(var early=await Session.Open()){
            await early.Sql("negative-control","BEGIN ISOLATION LEVEL READ COMMITTED");
            long wrong=await Scalar(early,"negative-control","SELECT (extract(epoch FROM transaction_timestamp())*1000000)::bigint");
            while(await Clock(observer,"negative-control")<=wrong+Retention)await Task.Delay(20);
            await using(var writer=await Session.Open())await Business(writer,observer,7,1);
            long commitLower=await Scalar(observer,"observer","SELECT "+Now);
            Require(await Scalar(early,"negative-control",SealSql.Replace("clock_timestamp()","transaction_timestamp()"),7,1,1)==1,"early-transaction-time mutant produces a seal");
            await early.Sql("negative-control","COMMIT");
            long anchor=await Scalar(observer,"observer","SELECT anchor_us FROM obligation WHERE partition=7");
            Require(anchor+Retention<commitLower,"mutant deadline already expired before first independently observed business commit");
            observations.Add(new{kind="early-clock-negative",anchorUs=anchor,deadlineUs=anchor+Retention,committedVisibilityUs=commitLower});
        }

        await using(var writer=await Session.Open())await Business(writer,observer,8,1);
        await observer.Sql("take","UPDATE partition SET epoch=2 WHERE id=8");
        await using(var stale=await Session.Open()){
            Require(await Seal(stale,8,1,1,"stale-control")==0,"real epoch guard rejects stale sealer");
            Require(await Scalar(stale,"missing-fence-negative",Batched(SealSql.Replace("AND epoch=$2","")),8,1,1)==1,"removing epoch guard lets stale sealer write; witness detects it");
        }
        try{await observer.Sql("immutable-negative","UPDATE obligation SET anchor_us=anchor_us+1 WHERE partition=1");throw new Exception("overwrite unexpectedly accepted");}
        catch(PostgresException e) when(e.SqlState=="23514"){Require(true,"database rejects direct sealed-anchor rewrite");}
        Save("PASS");
        await observer.Sql("setup","UPDATE fixture_run SET completed=true WHERE version=$1",FixtureVersion);
        Console.WriteLine("DONE bounded PostgreSQL protocol fixture; no kernel/provider capability or budget adopted");
    }
}
