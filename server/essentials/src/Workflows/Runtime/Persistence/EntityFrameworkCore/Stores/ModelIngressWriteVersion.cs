using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class ModelIngressWriteVersion
{
    internal static string For(RuntimeDbContext context, RuntimeModelExecutionState state, string? storedVersion) =>
        RuntimeSchemaWriteVersion.For(context, RuntimeModelStateEfModule.SchemaFamily,
            state.IngressOrigin is null ? RuntimeModelStateEfModule.SchemaVersion : RuntimeModelStateEfModule.IngressSchemaVersion, storedVersion);
}
