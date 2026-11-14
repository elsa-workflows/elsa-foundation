using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Checks required formats and selects the adopted stamp for tracked writes, preserving predecessor content.</summary>
internal static class RuntimeSchemaWriteVersion
{
    internal static string For(RuntimeDbContext context, string familyName, string minimum, string? storedVersion = null)
    {
        if (!string.IsNullOrEmpty(storedVersion) && Version.Parse(storedVersion) > Version.Parse(minimum))
            minimum = storedVersion;
        var gate = FindGate(context);
        if (gate is null) return minimum;
        var family = gate.StateOf(familyName)
            ?? throw new InvalidOperationException($"The {familyName} family has not been admitted by its schema gate.");
        if (family.WritesRefused)
            throw new EfSchemaFamilyWritesRefusedException(familyName, family.WriteVersion,
                family.UnreadableFinalizedVersion!, family.ReadableVersions);
        if (Version.Parse(family.WriteVersion) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(familyName, family.WriteVersion, minimum);
        return family.WriteVersion;
    }

    /// <summary>All writers in the protected EF ingress profile must be ready before any responsibility is accepted.</summary>
    internal static bool SupportsIngress(RuntimeDbContext context) =>
        CanWrite(context, RuntimePostCommitOutboxEfModule.SchemaFamily, RuntimePostCommitOutboxEfModule.SchemaVersion) &&
        CanWrite(context, RuntimeSchedulerWorkEfModule.SchemaFamily, RuntimeSchedulerWorkEfModule.IngressSchemaVersion) &&
        CanWrite(context, RuntimeWorkflowExecutionEfModule.SchemaFamily, RuntimeWorkflowExecutionEfModule.IngressSchemaVersion) &&
        CanWrite(context, RuntimeModelStateEfModule.SchemaFamily, RuntimeModelStateEfModule.IngressSchemaVersion) &&
        CanWrite(context, RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.ScopedPublicationSchemaVersion) &&
        CanWrite(context, RuntimeOperationalStateEfModule.SchemaFamily, RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion);

    private static bool CanWrite(RuntimeDbContext context, string familyName, string minimum)
    {
        var gate = FindGate(context);
        if (gate is not null && gate.StateOf(familyName) is null) return false;
        try { _ = For(context, familyName, minimum); return true; }
        catch (EfSchemaWriteRefusedException) { return false; }
    }

    private static EfSchemaModuleGate? FindGate(RuntimeDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());

    internal static string Artifact(RuntimeDbContext context, string? storedVersion = null) =>
        For(context, RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaVersion, storedVersion);
    internal static string Operational(RuntimeDbContext context, string? storedVersion = null) =>
        For(context, RuntimeOperationalStateEfModule.SchemaFamily, RuntimeOperationalStateEfModule.SchemaVersion, storedVersion);
}
