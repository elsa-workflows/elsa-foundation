using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("Probe compiles BpmnModelRuntime.cs from reviewed head; EF reader is prebuilt: " + typeof(EfWorkflowExecutableStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var access = new Access();
var state = new RuntimeModelExecutionState(1, new("tenant-a"), "execution", "tenant-a", new("elsa.bpmn",1),
    new("artifact","definition","definition-version","1.0.0","hash"),"incarnation",1,RuntimeModelLifecycle.Running,
    new("bpmn-runtime","actor"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,
    JsonSerializer.SerializeToElement(new { formatVersion=1, work="invalid-shape" }),null);
var runtime = new BpmnModelRuntime(null!,null!,null!,null!,null!,access,new Codec(),TimeProvider.System,null!,states:new Reader(state));
await Probe("BPMN ListWork persisted wrong-shape work", async()=>{_ = await runtime.ListWorkAsync("execution", new(), new("actor","tenant-a"));});
await using var context = new Context(new DbContextOptionsBuilder<Context>().UseSqlite("Data Source=:memory:").Options);
var readerType = typeof(EfWorkflowExecutableStore).Assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeModelExecutionReader")!;
var reader=(IRuntimeModelExecutionReader)Activator.CreateInstance(readerType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[context,access,new Codec()],null)!;
await Probe("EF Find missing model table", async()=>{_ = await reader.FindAsync("execution");});
await Probe("EF FindInput missing receipt table", async()=>{_ = await reader.FindInputAsync("execution","request");});
await Probe("EF ListPage missing model table", async()=>{_ = await reader.ListPageAsync(new());});
await Probe("Existing native Find missing table", async()=>{_ = await new EfWorkflowExecutionStateStore(context,access,new Codec()).FindAsync("execution");});
static async Task Probe(string name, Func<Task> action) { try { await action(); Console.WriteLine(name+": NO ERROR"); } catch(Exception ex) {Console.WriteLine(name+": "+ex.GetType().FullName+"; inner="+ex.InnerException?.GetType().FullName+"; message="+ex.Message);} }
sealed class Context(DbContextOptions options):RuntimeDbContext(options) {protected override void ConfigureProvider(ModelBuilder builder) {}}
sealed class Access:IPersistenceAccessContextAccessor {public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("tenant-a"));}
sealed class Codec:IRuntimeRecoveryContinuationCodec {public string Encode(string purpose,ReadOnlySpan<byte> payload)=>Convert.ToBase64String(payload);public byte[] Decode(string purpose,string token)=>Convert.FromBase64String(token);}
sealed class Reader(RuntimeModelExecutionState state):IRuntimeModelExecutionReader {public ValueTask<RuntimeModelExecutionState?> FindAsync(string id,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelExecutionState?>(state);public ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string id,string input,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelInputReceipt?>(null); public ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest req,CancellationToken ct=default)=>ValueTask.FromResult(new RuntimeStorePage<RuntimeModelExecutionState>(req,[state],null));}
