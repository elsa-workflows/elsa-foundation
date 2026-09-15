using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfPublicationSnapshotReviewProviderModelTests
{
    [Theory]
    [InlineData("Sqlite", typeof(PublishingSnapshotReviewSqliteDbContext), "INTEGER")]
    [InlineData("SqlServer", typeof(PublishingSnapshotReviewSqlServerDbContext), "bigint")]
    [InlineData("PostgreSql", typeof(PublishingSnapshotReviewPostgreSqlDbContext), "bigint")]
    [InlineData("MySql", typeof(PublishingSnapshotReviewMySqlDbContext), "bigint")]
    public void Provider_contexts_build_the_same_portable_model(string provider, Type contextType, string expiryColumnType)
    {
        using var context = CreateContext(provider);
        Assert.Equal(contextType, context.GetType());
        var entity = context.Model.FindEntityType("Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities.PublicationSnapshotReviewEntity")!;
        Assert.Equal(PublishingSnapshotReviewEfModule.TableName, entity.GetTableName());
        Assert.Equal(expiryColumnType, entity.FindProperty("ExpiresAt")!.GetColumnType());
        Assert.Equal("PreflightToken", entity.FindPrimaryKey()!.Properties.Single().Name);
    }

    private static PublishingSnapshotReviewDbContext CreateContext(string provider) => provider switch
    {
        "Sqlite" => new PublishingSnapshotReviewSqliteDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite("Data Source=:memory:").Options),
        "SqlServer" => new PublishingSnapshotReviewSqlServerDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewSqlServerDbContext>().UseSqlServer("Server=unused;Database=unused;User ID=unused;Password=unused").Options),
        "PostgreSql" => new PublishingSnapshotReviewPostgreSqlDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewPostgreSqlDbContext>().UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused").Options),
        "MySql" => new PublishingSnapshotReviewMySqlDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewMySqlDbContext>().UseMySQL("Server=unused;Database=unused;User=unused;Password=unused").Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
