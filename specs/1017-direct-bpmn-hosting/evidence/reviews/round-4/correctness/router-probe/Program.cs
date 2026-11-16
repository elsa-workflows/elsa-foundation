using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var results = new List<object>();
foreach (var unique in new[] { false, true })
{
    var file=Path.Combine(AppContext.BaseDirectory,(unique?"unique":"shared")+"-"+Guid.NewGuid().ToString("N")+".db");
    var access=new Access(); RuntimeSqliteDbContext Open()=>new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source="+file).Options);
    await using(var c=Open()) await c.Database.EnsureCreatedAsync();
    var seed=new WorkflowExecutableIdentity("pending","definition","v1","1.0.0","pending");
    var definition=JsonSerializer.SerializeToElement(new { process="leased-model" });
    var draft=new RuntimeModelMaterial(1,seed,new("test.model",1),"test-profile",definition,new(1,[]),[],[],[]);
    var hash=draft.ComputeContentHash(); var pin=seed with { ArtifactId=hash,ArtifactHash=hash };
    var model=new RuntimeModelMaterial(1,pin,draft.Protocol,draft.Profile,definition,draft.InputContract,[],[],[]);
    string incarnation;
    await using(var c=Open()) {var s=(IWorkflowExecutableStore)new EfWorkflowExecutableStore(c,access);await s.SaveModelAsync(model);incarnation=(await s.FindMaterialAsync(hash))!.IncarnationId;}
    var options=Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod=TimeSpan.Zero });
    await using(var c=Open())
    {
        var store=(IWorkflowExecutableStore)new EfWorkflowExecutableStore(c,access);
        var sources=new EfWorkflowExecutableSourceReferenceStore(c,access,new Codec());
        var at=DateTimeOffset.UtcNow;
        var reference=new WorkflowExecutableSourceReference("source-model",hash,"BpmnModel","source-model","1",pin.DefinitionId,pin.DefinitionVersionId,pin.ArtifactVersion,at,at,WorkflowExecutableReferenceScope.Published);
        await new WorkflowExecutableRootWriteLeaseManager(store,options,TimeProvider.System).ExecuteFencedAsync([pin],"initial-publication:"+Guid.NewGuid().ToString("N"),async(grants,t)=>{_ = await sources.SaveWithRootWriteLeasesAsync(reference,pin,grants,t);});
    }
    var firstEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSecond=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var stopFirst=new CancellationTokenSource();
    await using var firstContext=Open(); await using var secondContext=Open();

    object Router(RuntimeSqliteDbContext context,bool first)
    {
        IWorkflowExecutableStore store=new EfWorkflowExecutableStore(context,access);
        var actualQueue=new EfSchedulerWorkQueueStore(context,access,new Codec());
        var queue=DispatchProxy.Create<IWorkflowSchedulerWorkQueue,QueueGate>();
        var gate=(QueueGate)(object)queue; gate.Inner=actualQueue;gate.Entered=first?firstEntered:secondEntered;gate.Release=first?new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously):releaseSecond;
        var services=new ServiceCollection();services.AddScoped<ProbeDriver>();var provider=services.BuildServiceProvider();
        var assembly=typeof(WorkflowExecutableRootWriteLeaseManager).Assembly;
        Type Type(string name)=>assembly.GetType("Elsa.Workflows.Runtime.Services.Executions."+name,true)!;
        var registry=Activator.CreateInstance(Type("RuntimeExecutionProtocolRegistry"),true)!;
        Type("RuntimeExecutionProtocolRegistry").GetMethod("Initialize")!.Invoke(registry,[new[]{new RuntimeExecutionProtocolRegistration(model.Protocol,typeof(ProbeDriver)){InputKinds=["start"]}},services]);
        var readerType=typeof(EfWorkflowExecutableStore).Assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeModelExecutionReader",true)!;
        var reader=Activator.CreateInstance(readerType,[context,access,new Codec()])!;
        var native=new EfWorkflowExecutionStateStore(context,access,new Codec());
        var commits=new EfRuntimeCheckpointCommitStore(context,access);
        var selector=Activator.CreateInstance(Type("RuntimeExecutionProtocolSelector"),[registry,store,native,commits,access,provider,queue,reader])!;
        IWorkflowExecutableRootWriteLeaseManager leases=new WorkflowExecutableRootWriteLeaseManager(store,options,TimeProvider.System);
        if(unique)leases=new UniqueLeaseManager(leases);
        return Activator.CreateInstance(Type("RuntimeExecutionProtocolRouter"),[selector,null!,queue,new StubDrain(),new RuntimeExecutionOwnershipService(new InMemoryExecutionLivenessStateStore()),new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),null!,TimeProvider.System,leases,null!])!;
    }
    async Task Process(object router,string execution,CancellationToken token)
    {
        var now=DateTimeOffset.UtcNow;
        var command=new RuntimeModelExecutionCommand(pin,incarnation,"start","start",JsonSerializer.SerializeToElement(new { inputs=new {},sourceReferenceId="source-model" }),new("system",execution),null);
        var envelope=new WorkflowExecutionCommandEnvelope(execution,execution,new("start",execution,WorkflowExecutionCommandKind.ExecuteModel,now,JsonSerializer.SerializeToElement(command,new JsonSerializerOptions(JsonSerializerDefaults.Web)),new Dictionary<string,string>()),execution,WorkflowExecutionCommandDeliveryMode.AtLeastOnce,now);
        var result=(ValueTask<WorkflowExecutionCommandProcessResult>)router.GetType().GetMethod("ProcessAsync")!.Invoke(router,[envelope,WorkflowExecutionCommandDispatchOptions.Default,token])!;
        await result;
    }
    var firstTask=Process(Router(firstContext,true),"first-execution",stopFirst.Token); await firstEntered.Task;
    var secondTask=Process(Router(secondContext,false),"second-execution",CancellationToken.None); await secondEntered.Task;
    await stopFirst.CancelAsync(); string firstFailure;
    try {await firstTask;firstFailure="none";} catch(Exception ex){firstFailure=ex.GetType().Name;}
    int deleted; bool sourceRetired;
    await using(var c=Open())
    {
        var sources=new EfWorkflowExecutableSourceReferenceStore(c,access,new Codec());
        sourceRetired=await sources.RetireAsync("source-model",DateTimeOffset.UtcNow,"Qualification retirement while both initial admissions are pending");
        var gc=new WorkflowExecutableReferenceGarbageCollector(new EfWorkflowExecutableStore(c,access),sources,new EfWorkflowExecutionStateStore(c,access,new Codec()),options,TimeProvider.System,NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        deleted=(await gc.SweepAsync()).DeletedArtifactCount;
    }
    releaseSecond.SetResult();string secondFailure;
    try {await secondTask;secondFailure="none";}catch(Exception ex){secondFailure=ex.GetType().Name+": "+ex.Message;}
    await using var final=Open();var exists=await ((IWorkflowExecutableStore)new EfWorkflowExecutableStore(final,access)).FindMaterialAsync(hash)is not null;
    var pending=await new EfSchedulerWorkQueueStore(final,access,new Codec()).ListAsync(new("second-execution"));
    results.Add(new{uniqueLeaseIds=unique,firstFailure,sourceRetiredThroughPort=sourceRetired,artifactsDeletedBetweenAdmissions=deleted,secondFailure,pendingCommands=pending.Items.Count,materialPresentAfterAdmission=exists,database=file});
}
Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));

public class QueueGate:DispatchProxy
{
    public IWorkflowSchedulerWorkQueue Inner=null!;public TaskCompletionSource Entered=null!;public TaskCompletionSource Release=null!;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method!.Name==nameof(IWorkflowSchedulerWorkQueue.EnqueueAsync))return Enqueue((RuntimeSchedulerWorkItem)args![0]!, (CancellationToken)args[1]!);
        try{return method.Invoke(Inner,args);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}
    }
    async ValueTask<RuntimeSchedulerWorkItem> Enqueue(RuntimeSchedulerWorkItem work,CancellationToken token)
    {Entered.SetResult();await Release.Task.WaitAsync(token);return await Inner.EnqueueAsync(work,token);}
}
public sealed class ProbeDriver:IRuntimeExecutionProtocolDriver
{
    public ValueTask ValidateMaterialAsync(RuntimeModelMaterial m,CancellationToken t=default)=>ValueTask.CompletedTask;
    public ValueTask ValidateInputAsync(RuntimeModelExecutionContext c,CancellationToken t=default)=>ValueTask.CompletedTask;
    public ValueTask ExecuteAsync(RuntimeModelExecutionContext c,CancellationToken t=default)=>ValueTask.CompletedTask;
}
sealed class UniqueLeaseManager(IWorkflowExecutableRootWriteLeaseManager inner):IWorkflowExecutableRootWriteLeaseManager
{
    public ValueTask ExecuteAsync(string id,string lease,Func<CancellationToken,ValueTask> write,CancellationToken t=default)=>inner.ExecuteAsync(id,lease+":"+Guid.NewGuid().ToString("N"),write,t);
    public ValueTask ExecuteAsync(WorkflowExecutableIdentity pin,string lease,Func<CancellationToken,ValueTask> write,CancellationToken t=default)=>inner.ExecuteAsync(pin,lease+":"+Guid.NewGuid().ToString("N"),write,t);
}
sealed class StubDrain:IWorkflowSchedulerDrainer{public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest r,CancellationToken t=default)=>ValueTask.FromResult(new RuntimeSchedulerDrainResult(r.WorkflowExecutionId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,[]));}
sealed class Access:IPersistenceAccessContextAccessor{public PersistenceAccessContext Current{get;}=PersistenceAccessContext.Scoped(new("default"));}
sealed class Codec:IRuntimeRecoveryContinuationCodec{public string Encode(string p,ReadOnlySpan<byte>b)=>p+":"+Convert.ToBase64String(b);public byte[] Decode(string p,string t)=>Convert.FromBase64String(t[(p.Length+1)..]);}
