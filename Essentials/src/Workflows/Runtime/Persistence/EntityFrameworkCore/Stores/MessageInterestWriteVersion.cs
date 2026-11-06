using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class MessageInterestWriteVersion
{
    internal static string For(RuntimeDbContext context) => Require(context, RuntimeMessageInterestEfModule.SchemaFamily, RuntimeMessageInterestEfModule.SchemaVersion);
    // These existing owner families are touched by this qualified profile, including revision fences.
    internal static string Require(RuntimeDbContext context, string family, string supported)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(family);
        if (gate is not null && state is null) throw new NotSupportedException("The Message publication owner family is not admitted.");
        if (state is { WritesRefused: true }) throw new EfSchemaFamilyWritesRefusedException(family,
            state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        var version = state?.WriteVersion ?? supported;
        if (version != supported) throw new EfSchemaWriteRefusedException(family, version, supported);
        return version;
    }
}
