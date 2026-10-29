using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeNativeModelWorkWriteVersion
{
    public static string For(RuntimeDbContext context, WorkflowExecutionState state, string? storedVersion = null)
    {
        var minimum = state.ModelWorkOrigin is null ? RuntimeWorkflowExecutionEfModule.SchemaVersion : RuntimeWorkflowExecutionEfModule.ModelWorkSchemaVersion;
        if (storedVersion is not null && Version.Parse(storedVersion) > Version.Parse(minimum)) minimum = storedVersion;
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        if (gate is null) return minimum;
        var family = gate.StateOf(RuntimeWorkflowExecutionEfModule.SchemaFamily)
            ?? throw new InvalidOperationException("The native worker execution family has not been admitted by its schema gate.");
        if (family.WritesRefused)
            throw new EfSchemaFamilyWritesRefusedException(RuntimeWorkflowExecutionEfModule.SchemaFamily,
                family.WriteVersion, family.UnreadableFinalizedVersion!, family.ReadableVersions);
        if (Version.Parse(family.WriteVersion) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(RuntimeWorkflowExecutionEfModule.SchemaFamily, family.WriteVersion,
                minimum);
        return family.WriteVersion;
    }
}
