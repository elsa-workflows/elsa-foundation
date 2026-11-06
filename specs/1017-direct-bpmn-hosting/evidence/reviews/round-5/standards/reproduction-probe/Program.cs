using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

var assembly = typeof(WorkflowSchedulerDrainer).Assembly;
Console.WriteLine("Runtime assembly: " + assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
Console.WriteLine("Runtime file: " + assembly.Location);
foreach(var product in new[]{typeof(Elsa.Workflows.Runtime.Core.Models.RuntimeModelExecutionState).Assembly,typeof(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointCommitStore).Assembly}) Console.WriteLine(product.GetName().Name+": "+product.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var selectorType = assembly.GetType("Elsa.Workflows.Runtime.Services.Executions.RuntimeExecutionProtocolSelector", true)!;
var registryType = assembly.GetType("Elsa.Workflows.Runtime.Services.Executions.RuntimeExecutionProtocolRegistry", true)!;
var registry = Activator.CreateInstance(registryType, true);
var selector = Activator.CreateInstance(selectorType, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance, null,
    new object?[] { registry, Mock<IWorkflowExecutableStore>(), null, Mock<IRuntimeCheckpointCommitStore>(), null, null, null, Mock<IRuntimeModelExecutionReader>() }, null)!;
await Inspect("missing typed payload control", null);
await Inspect("malformed retained owned command", JsonSerializer.SerializeToElement("malformed-owned-command"));
var at = DateTimeOffset.UtcNow;
var owned = new RuntimeSchedulerWorkItem("work-r5", "execution-r5", "input-r5", WorkflowExecutionCommandKind.ExecuteModel,
    "envelope-r5", "idempotency-r5", at, at, payload: JsonSerializer.SerializeToElement("malformed-owned-command"));
var validationCalls = 0; var claimMutations = 0;
var queue = Custom<IWorkflowSchedulerWorkQueue>((method,args) => method.Name switch {
    "get_SupportsClaimTransitions" or "get_SupportsClaimValidation" => true,
    "ClaimAsync" => new ValueTask<RuntimeSchedulerWorkClaim?>(Claim((RuntimeSchedulerWorkClaimRequest)args![0]!)),
    _ => throw new Exception("Unexpected queue use: " + method.Name)
});
async Task<RuntimeSchedulerWorkClaim?> Claim(RuntimeSchedulerWorkClaimRequest request) {
    validationCalls++;
    await request.ValidateBeforeClaim!(owned, CancellationToken.None);
    claimMutations++;
    return null;
}
var nativeReader = Custom<IWorkflowExecutionStateStore>((method,args) => method.Name == "FindAsync" ? new ValueTask<WorkflowExecutionState?>((WorkflowExecutionState?)null) : throw new Exception("Unexpected native use: "+method.Name));
IWorkflowSchedulerDrainer drainer = new WorkflowSchedulerDrainer(queue, [], nativeReader, Mock<IWorkflowSchedulerPoisonStore>(), Mock<IWorkflowSchedulerPauseGate>());
typeof(WorkflowSchedulerDrainer).GetProperty("ModelProtocols", BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(drainer, selector);
try { await drainer.DrainAsync(new("execution-r5")); Console.WriteLine("public drainer: NO EXCEPTION"); }
catch(Exception e) { Console.WriteLine("public drainer: "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message);Console.WriteLine(e.StackTrace); }
Console.WriteLine($"Public drainer preclaim validation calls={validationCalls}, claim mutations={claimMutations}");

async Task Inspect(string name, JsonElement? payload) {
    var at=DateTimeOffset.UtcNow;
    var item = new RuntimeSchedulerWorkItem("work-r5", "execution-r5", "input-r5", WorkflowExecutionCommandKind.ExecuteModel,
        "envelope-r5", "idempotency-r5", at, at, payload:payload);
    try {
        var result=selectorType.GetMethod("SelectAsync")!.Invoke(selector,new object[]{item,CancellationToken.None})!;
        await (Task)result.GetType().GetMethod("AsTask")!.Invoke(result,null)!;
        Console.WriteLine(name+": NO EXCEPTION");
    } catch(Exception e) {
        while(e is TargetInvocationException && e.InnerException is not null)e=e.InnerException;
        Console.WriteLine(name+": "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message);
        Console.WriteLine(e.StackTrace);
    }
}
await ProviderProbe.RunAsync();
static T Mock<T>() where T:class => DispatchProxy.Create<T,YesProxy>();
static T Custom<T>(Func<MethodInfo,object?[]?,object?> behavior) where T:class { var proxy=DispatchProxy.Create<T,DelegateProxy>();((DelegateProxy)(object)proxy).Behavior=behavior;return proxy; }
public class DelegateProxy:DispatchProxy { public Func<MethodInfo,object?[]?,object?> Behavior=null!; protected override object? Invoke(MethodInfo? method,object?[]? args)=>Behavior(method!,args); }
public class YesProxy:DispatchProxy {
 protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType==typeof(bool) ? true : throw new Exception("Unexpected mock use: "+method?.Name);
}
