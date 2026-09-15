
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class BookmarkStateEfModule
{
    public const string TableName = "elsa_runtime_bookmark_state";
    public const string DefaultConnectionName = "ElsaRuntimeBookmarks";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-bookmarks.db";
    public const string SchemaVersion = "1.0.0";
    public const int WorkflowIdentityMaximumLength = 128;
    public const int BookmarkIdentityMaximumLength = 128;
    public const int StimulusTypeMaximumLength = 256;
    public const int StimulusHashMaximumLength = 450;
    public const int ProjectionMaximumLength = 450;
    // Five decimal digits per UTF-16 code unit plus a fixed-width length suffix.
    // The key contains digits only so relational provider collations cannot alter
    // ordinal ordering, while fixed padding preserves prefix ordering.
    public const int OrdinalOrderKeyMaximumLength = WorkflowIdentityMaximumLength * 5 + 5;
}
