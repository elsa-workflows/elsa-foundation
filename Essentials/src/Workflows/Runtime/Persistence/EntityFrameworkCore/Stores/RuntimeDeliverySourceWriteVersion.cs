using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Metadata seam for future canonical writers; this patch registers no writer or capability.</summary>
internal static class RuntimeDeliverySourceWriteVersion
{
    internal static string For(RuntimeDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimeDeliverySourceEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The delivery-source family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(RuntimeDeliverySourceEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        // Match existing direct-provider tools/tests. Normal hosts select their admitted schema gate.
        var version = state?.WriteVersion ?? RuntimeDeliverySourceEfModule.SchemaVersion;
        if (version != RuntimeDeliverySourceEfModule.SchemaVersion)
            throw new EfSchemaWriteRefusedException(RuntimeDeliverySourceEfModule.SchemaFamily, version,
                RuntimeDeliverySourceEfModule.SchemaVersion);
        return version;
    }
}
