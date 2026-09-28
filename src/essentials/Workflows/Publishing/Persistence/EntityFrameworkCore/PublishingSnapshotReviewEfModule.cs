using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

public static class PublishingSnapshotReviewEfModule
{
    public const string HistoryModuleName = "ElsaPublishingSnapshotReview";
    public const string TableName = "elsa_publication_snapshot_reviews";

    /// <summary>The persisted-schema version every snapshot-review row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";
    public const string ExpiryIndexName = "IX_elsa_publication_snapshot_reviews_expiresAt_preflightToken";
    public const int IdentityMaximumLength = 256;
    public const int CandidateHashMaximumLength = 128;
    public const int IncarnationMaximumLength = 32;
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
