using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;

namespace Elsa.Secrets.Persistence.Groundwork;

/// <summary>Contributes the secrets family's durable Groundwork declaration.</summary>
public sealed class SecretsGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-secrets";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(SecretsStorageManifest.Create());

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [typeof(ISecretRepository)],
            [],
            [],
            ["secrets-repository"]));
    }
}
