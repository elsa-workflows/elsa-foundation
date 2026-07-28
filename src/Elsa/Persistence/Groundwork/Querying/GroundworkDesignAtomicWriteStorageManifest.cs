using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Owns the scope-bound operation ledger shared by workflow and activity design persistence.
/// </summary>
public static class GroundworkDesignAtomicWriteStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const string FeatureIdentity = "elsa-design-atomic-write";
    public const string ManifestOwner = "elsa.design.atomic-write";
    public const string DesignOperationDocumentKind = "designOperation";
    public const string DesignOperationPhysicalTableName = DesignOperationDocumentKind;
    public const string AtomicWriteRouteIdentity = "design-atomic-write";
    public const string MultiDocumentTransactionsTopologyIdentity = "multi-document-transactions";

    public static StorageManifest Create()
    {
#pragma warning disable GW0001 // The physical declaration below is the authoritative storage contract.
        var operationUnit = new StorageUnit(
            new StorageUnitIdentity(DesignOperationDocumentKind),
            "Design operation ledger",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.AppendOnly,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);
#pragma warning restore GW0001

        operationUnit = operationUnit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(
                    PhysicalTableDefinition.DedicatedDocumentTable(DesignOperationPhysicalTableName)),
                [],
                [])
        };

        return new StorageManifest(
            new StorageManifestIdentity(FeatureIdentity),
            new StorageManifestOwner(ManifestOwner),
            new StorageManifestVersion(SchemaVersion),
            [operationUnit],
            new HashSet<string> { "optimistic-concurrency" },
            []);
    }
}

/// <summary>Contributes the shared design-operation ledger to the selected Groundwork deployment.</summary>
public sealed class GroundworkDesignAtomicWriteStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => GroundworkDesignAtomicWriteStorageManifest.FeatureIdentity;

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            GroundworkDesignAtomicWriteStorageManifest.Create(),
            [],
            [
                new GroundworkStorageRouteRequirement(
                    new StorageUnitIdentity(
                        GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind),
                    GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity,
                    new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit })
            ],
            [
                new GroundworkStorageTopologyRequirement(
                    GroundworkDesignAtomicWriteStorageManifest.MultiDocumentTransactionsTopologyIdentity)
            ],
            [GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity]));
    }
}
