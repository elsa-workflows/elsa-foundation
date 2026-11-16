using System.Data.Common;
using System.Reflection;
using Elsa.Workflows.Runtime.Services.Scheduler;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public static class ProviderProbe {
 public static async Task RunAsync() {
    var at = DateTimeOffset.UtcNow;
    await using var connection = new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options;
    var access = new Access();
    var protocol = new RuntimeExecutionProtocol("test.r5",1);
    var seed = new WorkflowExecutableIdentity("r5-artifact", "r5-process", "r5-version", "1.0.0", "pending");
    var definition = JsonSerializer.SerializeToElement(new { start="wait" });
    RuntimeModelMaterial Material(WorkflowExecutableIdentity identity) => new(1,identity,protocol,"r5",definition,new(1,[]),[],[],[]);
    var pin=seed with { ArtifactHash=Material(seed).ComputeContentHash() };
    var lease=new RuntimeExecutionLease("r5-lease","r5-execution","r5-owner",at,at.AddMinutes(10),1);
    string incarnation;
    await using(var setup=new RuntimeSqliteDbContext(options)) {
      await setup.Database.EnsureCreatedAsync();
      await new EfExecutionLivenessStateStore(setup,access,new Codec()).SaveAsync(new("ownership:r5-execution","r5-execution",lease,null,null,null));
      IWorkflowExecutableStore store=new EfWorkflowExecutableStore(setup,access);
      await store.SaveModelAsync(Material(pin));incarnation=(await store.FindMaterialAsync(pin.ArtifactId))!.IncarnationId;
    }
    var state=new RuntimeModelExecutionState(1,new("r5-scope"),"r5-execution",null,protocol,pin,incarnation,1,RuntimeModelLifecycle.Running,
      new("r5-system","r5-initiator"),at,at,JsonSerializer.SerializeToElement(new { tokens=new[]{"wait"}}),null);
    var result=JsonSerializer.SerializeToElement(new { status="Accepted" });
    var input=new RuntimeModelInputReceipt(new("r5-scope"),"r5-execution","r5-input","start","r5-content",1,at,null,null,result);
    var commit = new RuntimeCheckpointCommit("r5-commit",new("r5-checkpoint","ModelDecision","r5-execution",at,[],new Dictionary<string,string>()),
      new(null,null,[],[],[],[],[]),[],new Dictionary<string,string>()) { ExpectedFence=lease.ToFence(),ModelState=new(null,state,input) };
    var fault = new FailMarker();
    await using(var failing=new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).AddInterceptors(fault).Options)) {
      IRuntimeCheckpointCommitStore store=new EfRuntimeCheckpointCommitStore(failing,access,new Clock(at),new LeaseManager());
      try { await store.CommitAsync(commit,new(RuntimeCheckpointPersistenceMode.Immediate));Console.WriteLine("provider marker failure: NO EXCEPTION"); }
      catch(Exception e) { Console.WriteLine("provider marker failure: "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message);Console.WriteLine(e.StackTrace); }
    }
    Console.WriteLine("marker fault triggered="+fault.Triggered);
    await using(var check=new RuntimeSqliteDbContext(options)) {
      foreach(var table in new[]{"elsa_runtime_model_execution","elsa_runtime_model_input","elsa_runtime_checkpoint_commit"}) {
        await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM \""+table+"\"";Console.WriteLine(table+" count="+await cmd.ExecuteScalarAsync());
      }
      IRuntimeCheckpointCommitStore store=new EfRuntimeCheckpointCommitStore(check,access,new Clock(at),new LeaseManager());
      var accepted=await store.CommitAsync(commit,new(RuntimeCheckpointPersistenceMode.Immediate));Console.WriteLine("same valid commit without provider fault="+accepted.ToString());
    }
    await using(var physical=new RuntimeSqliteDbContext(options)) {
      var queue=new EfSchedulerWorkQueueStore(physical,access,new Codec());
      var work=new RuntimeSchedulerWorkItem("r5-work","r5-queued-model","r5-envelope-input",WorkflowExecutionCommandKind.ExecuteModel,
        "r5-envelope","r5-idempotency",at,at,payload:JsonSerializer.SerializeToElement("malformed-owned-command"));
      await queue.EnqueueAsync(work);
      var assembly=typeof(WorkflowSchedulerDrainer).Assembly;
      var registry=Activator.CreateInstance(assembly.GetType("Elsa.Workflows.Runtime.Services.Executions.RuntimeExecutionProtocolRegistry",true)!,true);
      var reader=(IRuntimeModelExecutionReader)Activator.CreateInstance(typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeModelExecutionReader",true)!,physical,access,new Codec())!;
      var nativeReader=new EfWorkflowExecutionStateStore(physical,access,new Codec());
      var checkpoints=new EfRuntimeCheckpointCommitStore(physical,access,new Clock(at),new LeaseManager());
      var selector=Activator.CreateInstance(assembly.GetType("Elsa.Workflows.Runtime.Services.Executions.RuntimeExecutionProtocolSelector",true)!,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,
        new object?[]{registry,new EfWorkflowExecutableStore(physical,access),nativeReader,checkpoints,access,null,queue,reader},null);
      IWorkflowSchedulerDrainer drainer=new WorkflowSchedulerDrainer(queue,[],nativeReader,DispatchProxy.Create<IWorkflowSchedulerPoisonStore,YesProxy>(),DispatchProxy.Create<IWorkflowSchedulerPauseGate,YesProxy>());
      typeof(WorkflowSchedulerDrainer).GetProperty("ModelProtocols",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(drainer,selector);
      try { await drainer.DrainAsync(new("r5-queued-model"));Console.WriteLine("physical EF queue drainer: NO EXCEPTION"); }
      catch(Exception e) {Console.WriteLine("physical EF queue drainer: "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message);Console.WriteLine(e.StackTrace);}
      var retained=await physical.SchedulerWorkItems.AsNoTracking().SingleAsync();Console.WriteLine($"physical queue retained: revision={retained.Revision}, owner={retained.ClaimOwnerId??"<null>"}, token={retained.ClaimToken}");
    }
 }
 private sealed class Access:IPersistenceAccessContextAccessor {public PersistenceAccessContext Current{get;}=PersistenceAccessContext.Scoped(new("r5-scope"));}
 private sealed class Clock(DateTimeOffset now):TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
 private sealed class LeaseManager:IWorkflowExecutableRootWriteLeaseManager { public ValueTask ExecuteAsync(string artifactId,string leaseId,Func<CancellationToken,ValueTask> write,CancellationToken cancellationToken=default)=>write(cancellationToken); }
 private sealed class Codec:IRuntimeRecoveryContinuationCodec { public string Encode(string purpose,ReadOnlySpan<byte> payload)=>Convert.ToBase64String(payload);public byte[] Decode(string purpose,string token)=>Convert.FromBase64String(token); }
 private sealed class FailMarker:DbCommandInterceptor {
   public bool Triggered;
   public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default) {
     if(command.CommandText.Contains("INSERT INTO \"elsa_runtime_checkpoint_commit\"",StringComparison.Ordinal)) {Triggered=true;throw new SqliteException("Synthetic provider unavailable during final marker",5);}
     return ValueTask.FromResult(result);
   }
 }
}
