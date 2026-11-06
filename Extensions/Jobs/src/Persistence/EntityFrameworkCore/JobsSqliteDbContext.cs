using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed class JobsSqliteDbContext(DbContextOptions<JobsSqliteDbContext> options) : JobsDbContext(options)
{
    protected override string ProviderName => EfProviderNames.Sqlite;
    protected override string TokenType => "BLOB";
    internal override Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken) => Database.SqlQueryRaw<long>(
        "SELECT 621355968000000000 + CAST(strftime('%s', 'now') AS INTEGER) * 10000000 " +
        "+ CAST(substr(strftime('%f', 'now'), 4, 3) AS INTEGER) * 10000 AS Value /* jobs.utc-clock */").SingleAsync(cancellationToken);
}
