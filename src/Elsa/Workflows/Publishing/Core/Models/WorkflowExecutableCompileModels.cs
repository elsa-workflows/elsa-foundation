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
    IReadOnlyDictionary<string, string>? CompatibilityMetadata = null)
{
    public WorkflowExecutableCompileSource? Source { get; init; }
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
    string? SourceVersion);
