using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.MongoDb;

namespace Elsa.Persistence.Groundwork.MongoDb;

/// <summary>
/// Compiles MongoDB's canonical target, including provider-specific bounded-mutation bindings and
/// selector definitions that participate in schema history and target fingerprints.
/// </summary>
public sealed class MongoDbGroundworkPhysicalSchemaTargetCompiler : IGroundworkPhysicalSchemaTargetCompiler
{
    public static MongoDbGroundworkPhysicalSchemaTargetCompiler Instance { get; } = new();

    public PhysicalSchemaTarget Compile(
        StorageManifest manifest,
        ProviderIdentity provider,
        IPhysicalNamePolicy namePolicy,
        IReadOnlyList<ExecutableStorageRoute> validatedRoutes)
    {
        ArgumentNullException.ThrowIfNull(validatedRoutes);
        return MongoDbPhysicalStorageModel.Compile(manifest, provider, namePolicy).Target;
    }
}
