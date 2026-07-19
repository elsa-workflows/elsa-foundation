using Elsa.Persistence.Groundwork.Composition;
using Elsa.Tagging.Core.Contracts;

namespace Elsa.Tagging.Persistence.Groundwork;

public sealed class TaggingGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-tagging";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            TaggingStorageManifest.Create(),
            [typeof(ITagDefinitionStore), typeof(ITagDefinitionAuditStore), typeof(IControlledTagValueStore), typeof(IControlledTagValueAuditStore)],
            [], [], ["tag-definition-catalog", "tag-definition-audit"]));
    }
}
