using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record WorkflowExecutableCompileRequest(
    string VersionId,
    WorkflowExecutableReferenceScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    string ArtifactIdPrefix,
    IReadOnlyDictionary<string, string>? CompatibilityMetadata = null,
    string? TenantId = null)
{
    /// <summary>Preserves the pre-tenant constructor shape for existing compiled callers.</summary>
    public WorkflowExecutableCompileRequest(
        string versionId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset createdAt,
        DateTimeOffset? publishedAt,
        DateTimeOffset? expiresAt,
        string artifactIdPrefix,
        IReadOnlyDictionary<string, string>? compatibilityMetadata)
        : this(
            versionId,
            scope,
            createdAt,
            publishedAt,
            expiresAt,
            artifactIdPrefix,
            compatibilityMetadata,
            TenantId: null)
    {
    }

    public WorkflowExecutableCompileSource? Source { get; init; }

    /// <summary>Preserves the pre-tenant positional deconstruction shape for source compatibility.</summary>
    public void Deconstruct(
        out string versionId,
        out WorkflowExecutableReferenceScope scope,
        out DateTimeOffset createdAt,
        out DateTimeOffset? publishedAt,
        out DateTimeOffset? expiresAt,
        out string artifactIdPrefix,
        out IReadOnlyDictionary<string, string>? compatibilityMetadata)
    {
        versionId = VersionId;
        scope = Scope;
        createdAt = CreatedAt;
        publishedAt = PublishedAt;
        expiresAt = ExpiresAt;
        artifactIdPrefix = ArtifactIdPrefix;
        compatibilityMetadata = CompatibilityMetadata;
    }
}

/// <summary>
/// The resolved source a compile runs against: the definition-version provenance plus the source-identity triple
/// (kind/id/version) used to stamp the resulting <see cref="WorkflowExecutableSourceReference"/>. The layout
/// sidecar is copied separately by the publish flow (it is not needed to compile behavior).
/// </summary>
public sealed record WorkflowExecutableCompileSource(
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    WorkflowDefinitionState State,
    string SourceKind,
    string SourceId,
    string? SourceVersion)
{
    public IReadOnlyCollection<Elsa.Workflows.Design.Core.Models.ActivityPresentationRecord>
        ActivityPresentation { get; init; } = [];
}
