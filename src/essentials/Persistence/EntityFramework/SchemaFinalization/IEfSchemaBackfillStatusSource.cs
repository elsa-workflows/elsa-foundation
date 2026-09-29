using Elsa.Persistence.Schema.SchemaFinalization;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// What a module's finalization gate reads of its post-finalization backfill (spec 186, FR-021): each family's status as
/// the backfill's worker last saw it. The gate knows the backfill only through this, so the backfill depends on the gate
/// and not the other way round, and neither is a public seam.
/// </summary>
internal interface IEfSchemaBackfillStatusSource
{
    /// <summary>What the backfill last saw of <paramref name="family"/>.</summary>
    EfSchemaBackfillStatus StatusOf(string family);
}
