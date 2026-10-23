using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>SQL Server mapping for the provider-neutral Rules runtime model.</summary>
internal sealed class RulesSqlServerDbContext(DbContextOptions<RulesSqlServerDbContext> options) : RulesDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.SqlServer;

    // One live clock expression at microsecond precision; the full supported date range fits bigint.
    internal override Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken) =>
        Database.SqlQueryRaw<long>(
            "SELECT DATEDIFF_BIG(MICROSECOND, CONVERT(datetime2(7), '0001-01-01T00:00:00.0000000'), " +
            "SYSUTCDATETIME()) * 10 AS Value /* rules.utc-clock */")
            .SingleAsync(cancellationToken);

    protected override void ConfigureProvider(ModelBuilder modelBuilder) =>
        RulesProviderMapping.Configure(modelBuilder, ExpectedProviderName, "nvarchar(max)", "varbinary(16)");
}
