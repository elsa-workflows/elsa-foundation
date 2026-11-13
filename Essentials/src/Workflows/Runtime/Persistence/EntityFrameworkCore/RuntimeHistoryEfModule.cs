using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>The Runtime module's single declared chain for journal and derived history projections.</summary>
internal static class RuntimeHistoryEfModule
{
    internal const string SchemaFamily = "RuntimeHistory";
    internal const string SchemaVersion = "1.0.0";
    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryEfModule).Assembly, SchemaFamily);
}
