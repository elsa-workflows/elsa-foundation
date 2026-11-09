using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var checks = new List<object>(); var failures = 0;
foreach(var kind in new[]{"io", "unrequested-cancellation", "provider", "requested-cancellation", "existing-retention-io"})
{
    var cause = kind == "unrequested-cancellation" ? (Exception)new OperationCanceledException("Injected physical provider cancellation") : new IOException("Injected physical read IO failure");
    var interceptor = new ReadFailure(cause);
    await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); await connection.OpenAsync();
    await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options);
    if(kind != "provider") await context.Database.EnsureCreatedAsync();
    var store = new EfRuntimePostCommitOutboxStore(context, new Access());
    interceptor.Armed = kind is "io" or "unrequested-cancellation" or "existing-retention-io";
    using var requested = new CancellationTokenSource(); if(kind == "requested-cancellation") requested.Cancel();
    Exception? observed = null;
    try
    {
        if(kind == "existing-retention-io") await store.ListPinnedExecutableArtifactIdsAsync();
        else await ((IPostCommitOutboxLookupStore)store).FindUndeliveredDurableAsync("owned-worker", "worker.result", requested.Token);
    }
    catch(Exception error){ observed = error; }
    var expected = kind switch {
        "requested-cancellation" => observed is OperationCanceledException c && c.CancellationToken == requested.Token,
        "existing-retention-io" => observed is StimulusDeliveryRetentionException e && ReferenceEquals(e.InnerException,cause),
        "provider" => observed is RuntimeSchedulerWorkUnavailableException p && p.InnerException is DbException,
        _ => observed is RuntimeSchedulerWorkUnavailableException d && ReferenceEquals(d.InnerException,cause) && d.Message.Contains("owned-worker") && d.Message.Contains("boundary-scope")
    };
    if(!expected) failures++;
    checks.Add(new{kind,expectedDomainBoundary=expected,exception=observed?.GetType().FullName,inner=observed?.InnerException?.GetType().FullName,message=observed?.Message});
}
var product=typeof(EfRuntimePostCommitOutboxStore).Assembly;
Console.WriteLine(JsonSerializer.Serialize(new{executed=checks.Count,passed=checks.Count-failures,failed=failures,productSource=product.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,checks},new JsonSerializerOptions{WriteIndented=true}));
return failures == 0 ? 0 : 1;
sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("boundary-scope")); }
sealed class ReadFailure(Exception cause) : DbCommandInterceptor {
    public bool Armed {get;set;}
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default) {
        if(Armed) throw cause; return ValueTask.FromResult(result);
    }
}
