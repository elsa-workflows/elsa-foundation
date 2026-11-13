using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>The Runtime module's single declared chain for journal and derived history projections.</summary>
internal static class RuntimeHistoryEfModule
{
    internal const string SchemaFamily = "RuntimeHistory";
    internal const string SchemaVersion = "1.0.0";
    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryEfModule).Assembly, SchemaFamily);

    // Metadata only: capability checks never inspect history rows or activate execution work.
    internal static bool HasProjectionModel(RuntimeDbContext context) => Chain.ContentColumns.Any() &&
        Chain.ContentColumns.All(column => context.Model.FindEntityType(column.Entity)?.FindProperty(column.Name) is not null);
}
