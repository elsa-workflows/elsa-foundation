using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var results = new List<object>();
foreach (var operation in new[] { "FindMaterialAsync", "ListMaterialPageAsync", "SaveModelAsync" })
foreach (var failure in new[] { "provider", "io", "unrequested-cancellation", "requested-cancellation" })
{
    Exception cause = failure == "unrequested-cancellation" ? new OperationCanceledException("Unrequested authoritative material read cancellation.") : new IOException("Authoritative material read failed.");
    var interceptor = new FailOpen(cause);
    var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:");
    if (failure != "provider") builder.AddInterceptors(interceptor);
    await using var context = new RuntimeSqliteDbContext(builder.Options);
    IWorkflowExecutableStore store = new EfWorkflowExecutableStore(context, new Access());
    using var cancel = new CancellationTokenSource();
    if (failure == "requested-cancellation") cancel.Cancel();
    Exception? seen = null;
    try {
        if (operation == "FindMaterialAsync") await store.FindMaterialAsync("requested-model", cancel.Token);
        else if (operation == "ListMaterialPageAsync") await store.ListMaterialPageAsync(new(), cancel.Token);
        else {
            var definition = JsonSerializer.SerializeToElement(new { formatVersion = 1, immutableBody = "probe" });
            var seed = new WorkflowExecutableIdentity("pending", "pending", "pending", "1.0.0", "pending");
            var material = new RuntimeModelMaterial(1, seed, new("probe.model", 1), "probe-v1", definition, new(1, []), [], [], []);
            var hash = material.ComputeContentHash();
            var pin = new WorkflowExecutableIdentity(hash, "probe-definition", "probe-version", "1.0.0", hash);
            material = new(1, pin, material.Protocol, material.Profile, material.Definition, material.InputContract, [], [], []);
            await store.SaveModelAsync(material, cancel.Token);
        }
    } catch (Exception ex) { seen = ex; }
    results.Add(new { operation, failure, exception = seen?.GetType().FullName, causePreservedAsInner = ReferenceEquals(cause, seen?.InnerException), escapedSameCause = ReferenceEquals(cause, seen), requestedTokenPreserved = seen is OperationCanceledException oce && oce.CancellationToken == cancel.Token, attempts = interceptor.Attempts, message = seen?.Message });
}
// Existing native route is an explicit comparison, not a new PR finding.
{
 var cause = new IOException("Existing native artifact read failure."); var interceptor = new FailOpen(cause);
 await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").AddInterceptors(interceptor).Options);
 IWorkflowExecutableStore store = new EfWorkflowExecutableStore(context,new Access());
 try { await store.FindAsync("requested-native"); } catch(Exception seen) { results.Add(new { operation="FindAsync-existing-native",exception=seen.GetType().FullName,escapedSameCause=ReferenceEquals(cause,seen),attempts=interceptor.Attempts }); }
}
var products = new[]{ typeof(EfWorkflowExecutableStore).Assembly, typeof(IWorkflowExecutableStore).Assembly, typeof(RuntimeSqliteDbContext).Assembly, typeof(EfRelationalExceptionClassifier).Assembly }.Distinct().Select(a=>new { a.Location, informationalVersion=a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a.Location))) });
Console.WriteLine(JsonSerializer.Serialize(new { source = "d8969d8e5272b00cb403477ead0cd9521a67f651", qualifiedRelevantTree = "9d4221e872c7f991e3a46919d8810a97ffee4e83", products,results},new JsonSerializerOptions{WriteIndented=true}));
sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("tenant-a")); }
sealed class FailOpen(Exception cause) : DbConnectionInterceptor
{
 public int Attempts {get;private set;}
 public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken=default) { Attempts++; return ValueTask.FromException<InterceptionResult>(cause); }
}
