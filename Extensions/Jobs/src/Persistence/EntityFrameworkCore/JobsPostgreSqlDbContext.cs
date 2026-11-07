using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed class JobsPostgreSqlDbContext(DbContextOptions<JobsPostgreSqlDbContext> options) : JobsDbContext(options)
{
    protected override string ProviderName => EfProviderNames.PostgreSql;
    protected override string TokenType => "bytea";
    internal override Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken) => Database.SqlQueryRaw<long>(
        "SELECT (TRUNC(EXTRACT(EPOCH FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000) AS \"Value\" /* jobs.utc-clock */").SingleAsync(cancellationToken);
}
