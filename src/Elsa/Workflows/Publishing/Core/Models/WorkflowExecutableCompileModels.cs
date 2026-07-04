using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record WorkflowExecutableCompileRequest(
    string VersionId,
    WorkflowExecutableScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    string ArtifactIdPrefix,
    IReadOnlyDictionary<string, string>? CompatibilityMetadata = null)
{
    public WorkflowExecutableCompileSource? Source { get; init; }
}

public sealed record WorkflowExecutableCompileSource(
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    WorkflowDefinitionState State,
    WorkflowExecutableSourceReference SourceReference);
