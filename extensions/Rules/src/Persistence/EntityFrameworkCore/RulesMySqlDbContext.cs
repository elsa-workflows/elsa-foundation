using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>MySQL mapping for the provider-neutral Rules runtime model.</summary>
internal sealed class RulesMySqlDbContext(DbContextOptions<RulesMySqlDbContext> options) : RulesDbContext(options)
{
    internal const string ExpectedProviderName = EfProviderNames.MySql;

    // UTC_TIMESTAMP(6) supplies microsecond precision; multiplying TIMESTAMPDIFF by ten stores .NET ticks exactly.
    internal override Task<long> ReadUtcTicksAsync(CancellationToken cancellationToken) =>
        Database.SqlQueryRaw<long>(
            "SELECT CAST(621355968000000000 + TIMESTAMPDIFF(MICROSECOND, " +
            "CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), UTC_TIMESTAMP(6)) * 10 AS SIGNED) " +
            "AS Value /* rules.utc-clock */")
            .SingleAsync(cancellationToken);

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation("MySQL:Charset", "utf8mb4");
        RulesProviderMapping.Configure(modelBuilder, ExpectedProviderName, "longtext", "varbinary(16)");
    }
}
