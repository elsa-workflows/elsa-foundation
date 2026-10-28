using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeSchedulerWorkWriteVersion
{
    public static string For(RuntimeDbContext context, RuntimeSchedulerWorkItem item, string? storedVersion = null)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimeSchedulerWorkEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The scheduler-work family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(RuntimeSchedulerWorkEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        var version = state?.WriteVersion ?? RuntimeSchedulerWorkEfModule.SchemaVersion;
        if ((item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel || storedVersion == RuntimeSchedulerWorkEfModule.SchemaVersion) &&
            version != RuntimeSchedulerWorkEfModule.SchemaVersion)
            throw new EfSchemaWriteRefusedException(RuntimeSchedulerWorkEfModule.SchemaFamily, version,
                RuntimeSchedulerWorkEfModule.SchemaVersion);
        return version;
    }
}
