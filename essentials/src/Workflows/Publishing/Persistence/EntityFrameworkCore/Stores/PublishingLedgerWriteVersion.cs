using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>All ledger writers use the admitted family stamp; new replay evidence requires version 2.</summary>
internal static class PublishingLedgerWriteVersion
{
    internal static string For(PublishingSnapshotReviewDbContext context, string? storedVersion = null, bool hasFingerprint = false)
    {
        var chain = PublishingLedgerEfModule.Chain;
        var versions = chain.ReadableVersions.ToList();
        var minimum = hasFingerprint ? PublishingLedgerEfModule.ContentSchemaVersion : PublishingLedgerEfModule.LegacyContentSchemaVersion;
        if (storedVersion is not null)
        {
            EfSchemaVersion.EnsureReadable(chain, storedVersion);
            if (versions.IndexOf(storedVersion) > versions.IndexOf(minimum)) minimum = storedVersion;
        }
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        if (gate is null) return chain.CurrentVersion;
        var family = gate.StateOf(chain.Family)
            ?? throw new InvalidOperationException("The PublishingLedger family has not been admitted by its schema gate.");
        if (family.WritesRefused)
            throw new EfSchemaFamilyWritesRefusedException(chain.Family, family.WriteVersion,
                family.UnreadableFinalizedVersion!, family.ReadableVersions);
        EfSchemaVersion.EnsureReadable(chain, family.WriteVersion);
        if (versions.IndexOf(family.WriteVersion) < versions.IndexOf(minimum))
            throw new EfSchemaWriteRefusedException(chain.Family, family.WriteVersion, minimum);
        return family.WriteVersion;
    }
}
