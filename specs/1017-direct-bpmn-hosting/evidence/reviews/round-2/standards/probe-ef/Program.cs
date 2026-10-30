using System.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
Console.WriteLine("EfRuntimeModelExecutionReader.cs compiled from bbb21b25b1a2fa40c900d4374c2a979ff21377f4");
await using var context = new Context(new DbContextOptionsBuilder<Context>().UseSqlite("Data Source=:memory:").Options);
var access = new Access();
var reader=new EfRuntimeModelExecutionReader(context,access,new Codec());
await Probe("EF Find missing model table",async()=>{_=await reader.FindAsync("execution");});
await Probe("EF FindInput missing receipt table",async()=>{_=await reader.FindInputAsync("execution","request");});
await Probe("EF ListPage missing model table",async()=>{_=await reader.ListPageAsync(new());});
await Probe("Existing native Find missing table",async()=>{_=await new EfWorkflowExecutionStateStore(context,access,new Codec()).FindAsync("execution");});
static async Task Probe(string name,Func<Task> action) {try{await action();Console.WriteLine(name+": NO ERROR");}catch(Exception ex){Console.WriteLine(name+": "+ex.GetType().FullName+"; inner="+ex.InnerException?.GetType().FullName+"; message="+ex.Message);}}
sealed class Context(DbContextOptions options):RuntimeDbContext(options){protected override void ConfigureProvider(ModelBuilder builder){}}
sealed class Access:IPersistenceAccessContextAccessor{public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("tenant-a"));}
sealed class Codec:IRuntimeRecoveryContinuationCodec{public string Encode(string purpose,ReadOnlySpan<byte> payload)=>Convert.ToBase64String(payload);public byte[] Decode(string purpose,string token)=>Convert.FromBase64String(token);}
