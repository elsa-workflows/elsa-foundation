using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Compiles the provider-canonical physical target after Elsa has validated the selected manifest,
/// host naming policy and executable routes. Providers use this seam when their durable target
/// includes definitions beyond the provider-neutral route set.
/// </summary>
public interface IGroundworkPhysicalSchemaTargetCompiler
{
    PhysicalSchemaTarget Compile(
        StorageManifest manifest,
        ProviderIdentity provider,
        IPhysicalNamePolicy namePolicy,
        IReadOnlyList<ExecutableStorageRoute> validatedRoutes);
}

/// <summary>The canonical route-only target compiler used by relational Groundwork providers.</summary>
public sealed class GroundworkRoutePhysicalSchemaTargetCompiler : IGroundworkPhysicalSchemaTargetCompiler
{
    public static GroundworkRoutePhysicalSchemaTargetCompiler Instance { get; } = new();

    public PhysicalSchemaTarget Compile(
        StorageManifest manifest,
        ProviderIdentity provider,
        IPhysicalNamePolicy namePolicy,
        IReadOnlyList<ExecutableStorageRoute> validatedRoutes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(namePolicy);
        ArgumentNullException.ThrowIfNull(validatedRoutes);
        return new PhysicalSchemaTarget(manifest.Identity, manifest.Version, provider, validatedRoutes);
    }
}
