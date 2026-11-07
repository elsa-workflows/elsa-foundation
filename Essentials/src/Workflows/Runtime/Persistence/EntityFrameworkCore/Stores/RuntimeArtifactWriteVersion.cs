using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Ordinary compatible writes retain the admitted predecessor; ingress requires the retention reader.</summary>
internal static class RuntimeArtifactWriteVersion
{
    public static string For(RuntimeDbContext context, bool requiresIngress = false)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimeArtifactEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The material family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(RuntimeArtifactEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        var version = state?.WriteVersion ?? RuntimeArtifactEfModule.SchemaVersion;
        if (requiresIngress && version != RuntimeArtifactEfModule.SchemaVersion)
            throw new EfSchemaWriteRefusedException(RuntimeArtifactEfModule.SchemaFamily, version, RuntimeArtifactEfModule.SchemaVersion);
        return version;
    }
}
