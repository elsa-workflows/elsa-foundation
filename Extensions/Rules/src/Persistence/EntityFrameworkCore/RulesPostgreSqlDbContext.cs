using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>PostgreSQL mapping for the provider-neutral Rules runtime model.</summary>
internal sealed class RulesPostgreSqlDbContext(DbContextOptions<RulesPostgreSqlDbContext> options) : RulesDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.PostgreSql;

    // clock_timestamp observes wall time even inside a long transaction; EXTRACT returns numeric, avoiding float tick loss.
    internal override Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken) =>
        Database.SqlQueryRaw<long>(
            "SELECT (TRUNC(EXTRACT(EPOCH FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000) " +
            "AS \"Value\" /* rules.utc-clock */")
            .SingleAsync(cancellationToken);

    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        RulesProviderMapping.Configure(modelBuilder, ExpectedProviderName, "text", "bytea");
}
