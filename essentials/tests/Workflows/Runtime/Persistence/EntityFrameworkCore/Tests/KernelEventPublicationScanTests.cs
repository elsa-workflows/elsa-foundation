using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class KernelEventPublicationScanTests
{
    [Fact]
    public async Task Sqlite_keeps_Event_publication_passes_finite_across_late_commits()
    {
        await using var database = new TemporarySqliteDatabase("kernel-event-publication-scan");
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString).Options;
        await KernelEventPublicationScanContract.RunAsync(() => new RuntimeSqliteDbContext(options),
            interceptor => new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>(options)
                .AddInterceptors(interceptor).Options));
    }
}
