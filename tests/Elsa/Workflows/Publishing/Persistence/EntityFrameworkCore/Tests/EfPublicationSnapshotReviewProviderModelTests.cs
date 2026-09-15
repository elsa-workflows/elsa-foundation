using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
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

        var policy = context.Model.FindEntityType(typeof(PublicationPolicyEntity))!;
        Assert.Equal(PublishingPolicyProjectionEfModule.PolicyTableName, policy.GetTableName());
        Assert.Equal(PublishingPolicyProjectionEfModule.IdentityMaximumLength, policy.FindProperty(nameof(PublicationPolicyEntity.DefaultSlotName))!.GetMaxLength());
        Assert.Contains(policy.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(PublicationPolicyEntity.TenantIdHash),
                nameof(PublicationPolicyEntity.PolicyKeyHash),
                nameof(PublicationPolicyEntity.PolicyKey)]));

        var intent = context.Model.FindEntityType(typeof(PublicationProjectionIntentEntity))!;
        Assert.Equal(PublishingPolicyProjectionEfModule.ProjectionIntentTableName, intent.GetTableName());
        Assert.Equal(PublishingPolicyProjectionEfModule.IdentityMaximumLength, intent.FindProperty(nameof(PublicationProjectionIntentEntity.IntentId))!.GetMaxLength());
        Assert.Equal(PublishingPolicyProjectionEfModule.IntentIdOrderKeyMaximumLength, intent.FindProperty(nameof(PublicationProjectionIntentEntity.IntentIdOrderKey))!.GetMaxLength());
        Assert.Contains(intent.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(PublicationProjectionIntentEntity.TenantIdHash),
                nameof(PublicationProjectionIntentEntity.PublicationIdHash),
                nameof(PublicationProjectionIntentEntity.IntentIdOrderKey),
                nameof(PublicationProjectionIntentEntity.IntentIdHash)]));
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
