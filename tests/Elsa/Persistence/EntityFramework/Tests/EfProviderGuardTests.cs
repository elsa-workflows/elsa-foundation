using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfProviderGuardTests
{
    [Fact]
    public void Ensure_allows_a_matching_provider()
    {
        EfProviderGuard.Ensure(EfProviderNames.Sqlite, EfProviderNames.Sqlite, "SecretsSqliteDbContext");
    }

    [Fact]
    public void Ensure_refuses_a_mismatched_provider()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfProviderGuard.Ensure(EfProviderNames.Sqlite, EfProviderNames.PostgreSql, "SecretsPostgreSqlDbContext"));
        Assert.Contains(EfProviderNames.Sqlite, exception.Message, StringComparison.Ordinal);
        Assert.Contains(EfProviderNames.PostgreSql, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ensure_refuses_a_missing_provider_name()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfProviderGuard.Ensure(null, EfProviderNames.Sqlite, "SecretsSqliteDbContext"));
        Assert.Contains("<none>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ensure_on_a_live_context_reads_Database_ProviderName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-ef-guard-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<GuardTestContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using var context = new GuardTestContext(options);
            EfProviderGuard.Ensure(context, EfProviderNames.Sqlite);
            var mismatch = Assert.Throws<InvalidOperationException>(() =>
                EfProviderGuard.Ensure(context, EfProviderNames.SqlServer));
            Assert.Contains(nameof(GuardTestContext), mismatch.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class GuardTestContext(DbContextOptions<GuardTestContext> options) : DbContext(options);
}
