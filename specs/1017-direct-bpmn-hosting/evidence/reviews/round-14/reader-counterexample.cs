using System.Data.Common;
using System.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

foreach (var operation in new[] {"state", "input", "page"})
foreach (var kind in new[] {"io", "unrequested-cancel", "requested-cancel"})
{
    var failure = kind == "io" ? (Exception)new IOException("injected authoritative read I/O") : new OperationCanceledException("injected read cancellation");
    var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").AddInterceptors(new Failure(failure)).Options;
    await using var context = new RuntimeSqliteDbContext(options);
    var method = typeof(EfRuntimeModelCheckpointTests).GetMethod("Reader", BindingFlags.Static|BindingFlags.NonPublic)!;
    var reader = (IRuntimeModelExecutionReader)method.Invoke(null, new object[]{context, "tenant-a"})!;
    using var cancellation = new CancellationTokenSource();
    if (kind == "requested-cancel") cancellation.Cancel();
    try {
        if(operation == "state") await reader.FindAsync("requested-execution", cancellation.Token);
        else if(operation == "input") await reader.FindInputAsync("requested-execution", "requested-input", cancellation.Token);
        else await reader.ListPageAsync(new(), cancellation.Token);
        Console.WriteLine($"{operation}/{kind}: NO ERROR");
    }
    catch (Exception e) { Console.WriteLine($"{operation}/{kind}: {e.GetType().FullName}; same-cause={ReferenceEquals(failure,e)}; inner={e.InnerException?.GetType().FullName}; message={e.Message}"); }
}
sealed class Failure(Exception error):DbConnectionInterceptor
{
 public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromException<InterceptionResult>(error);
}
