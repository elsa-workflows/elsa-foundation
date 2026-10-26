using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Emit the gate's observed format; new protocol data never fits a predecessor family.</summary>
internal static class RuntimePostCommitOutboxWriteVersion
{
    public static string For(RuntimeDbContext context, RuntimePostCommitOutboxItem item)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimePostCommitOutboxEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The outbox family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(RuntimePostCommitOutboxEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        // Direct ungated provider construction is used by tools/tests; normal hosts always select their admitted gate.
        var version = state?.WriteVersion ?? RuntimePostCommitOutboxEfModule.SchemaVersion;
        if (item.StimulusDeliveryProtocolVersion is not null && version != RuntimePostCommitOutboxEfModule.SchemaVersion)
            throw new EfSchemaWriteRefusedException(RuntimePostCommitOutboxEfModule.SchemaFamily, version,
                RuntimePostCommitOutboxEfModule.SchemaVersion);
        return version;
    }
}
