using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public static class BookmarkStateEfModule
{
    public const string TableName = "elsa_runtime_bookmark_state";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "BookmarkState";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(BookmarkStateEfModule).Assembly, SchemaFamily);
    public const int WorkflowIdentityMaximumLength = 128;
    public const int BookmarkIdentityMaximumLength = 128;
    public const int StimulusTypeMaximumLength = 256;
    public const int StimulusHashMaximumLength = 450;
    public const int ProjectionMaximumLength = 450;
    // Five decimal digits per UTF-16 code unit plus a fixed-width length suffix.
    // The key contains digits only so relational provider collations cannot alter
    // ordinal ordering, while fixed padding preserves prefix ordering.
    public const int OrdinalOrderKeyMaximumLength = WorkflowIdentityMaximumLength * 5 + 5;

    /// <summary>
    /// The covering index the HTTP route-table convergence check reads waiting bookmarks through (#2190): keyed on
    /// scope hash, type lookup key and expiry, with the stimulus lookup key and hash included (or appended to the key
    /// where the provider has no INCLUDE).
    /// </summary>
    public const string RouteConvergenceIndexName = "IX_elsa_runtime_bookmark_state_route_convergence";
}
