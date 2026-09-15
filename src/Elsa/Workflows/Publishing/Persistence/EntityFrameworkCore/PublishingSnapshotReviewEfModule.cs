using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

public static class PublishingSnapshotReviewEfModule
{
    public const string TableName = "elsa_publication_snapshot_reviews";
    public const string ExpiryIndexName = "IX_elsa_publication_snapshot_reviews_expiresAt_preflightToken";
    public const int IdentityMaximumLength = 256;
    public const int CandidateHashMaximumLength = 128;
    public const string DefaultConnectionName = "ElsaPublishing";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-publishing.db";
    public static string HistoryTableName => EfMigrationsHistory.TableName("ElsaPublishingSnapshotReview");
}
