using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record WorkflowExecutableCompileRequest(
    string VersionId,
    WorkflowExecutableScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    string ArtifactIdPrefix,
    IReadOnlyDictionary<string, string>? CompatibilityMetadata = null);
