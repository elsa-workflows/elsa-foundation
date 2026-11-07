using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Selects the observed artifact-family stamp without the paused trigger feature.</summary>
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
        {
            // A tracked rewrite must reach the existing SaveChanges check: it confirms a newer
            // ORIGINAL row stamp against the durable record on the actual writing context. This
            // selection is not finalization authority, and new rows cannot assert a newer minimum.
            if (storedVersion is not null && storedVersion == minimum && family.ReadableVersions.Contains(storedVersion, StringComparer.Ordinal))
                return storedVersion;
            throw new EfSchemaWriteRefusedException(familyName, family.WriteVersion, minimum);
        }
        return family.WriteVersion;
    }

    private static EfSchemaModuleGate? FindGate(RuntimeDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());

    internal static async ValueTask ConfirmArtifactUpdateAsync(RuntimeDbContext context, string storedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FindGate(context) is { } gate)
        {
            var family = gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)
                ?? throw new InvalidOperationException("The artifact family has not been admitted by its schema gate.");
            // ExecuteUpdate bypasses SaveChanges. Confirm only this durable family on the same
            // context/transaction: full refresh can publish through a separate membership writer.
            if (Version.Parse(storedVersion) > Version.Parse(family.WriteVersion))
                await gate.ConfirmAsync(context, RuntimeArtifactEfModule.SchemaFamily, cancellationToken);
            _ = For(context, RuntimeArtifactEfModule.SchemaFamily, storedVersion);
        }
    }

    internal static string Artifact(RuntimeDbContext context, string? storedVersion = null) =>
        For(context, RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaVersion, storedVersion);
}
