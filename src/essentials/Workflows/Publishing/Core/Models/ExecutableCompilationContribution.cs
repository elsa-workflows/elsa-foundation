using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>The immutable context visible to executable-compilation sources.</summary>
public sealed record ExecutableCompilationContext(
    WorkflowExecutableCompileRequest Request,
    WorkflowExecutableCompileSource Source,
    ExecutableNode RootActivity)
{
    /// <summary>The publication tenant scope. A null value denotes the host's unscoped/default tenant.</summary>
    public string? TenantId => Request.TenantId;
}

/// <summary>One metadata key/value claimed for one compiled executable node.</summary>
public sealed record ExecutableNodeMetadataClaim(
    string ExecutableNodeId,
    string Key,
    string Value);

/// <summary>One exact child artifact claimed by one compiled executable node.</summary>
public sealed record ExecutableDependencyClaim(
    string ExecutableNodeId,
    string ArtifactId,
    string ArtifactHash);

/// <summary>
/// Immutable node-metadata and exact-dependency claims returned by one compilation source.
/// </summary>
public sealed record ExecutableCompilationContribution
{
    public ExecutableCompilationContribution(
        IReadOnlyCollection<ExecutableNodeMetadataClaim>? nodeMetadata = null,
        IReadOnlyCollection<ExecutableDependencyClaim>? dependencies = null)
    {
        NodeMetadata = Array.AsReadOnly((nodeMetadata ?? []).ToArray());
        Dependencies = Array.AsReadOnly((dependencies ?? []).ToArray());
    }

    /// <summary>Deterministic identity stamped by the single Publishing-owned aggregating handler.</summary>
    public string? SourceIdentity { get; init; }

    public IReadOnlyCollection<ExecutableNodeMetadataClaim> NodeMetadata { get; }
    public IReadOnlyCollection<ExecutableDependencyClaim> Dependencies { get; }
}

/// <summary>
/// The normalized result of the single compile fan-in pass: an enriched executable tree plus exact dependency
/// claims. Keeping both results together prevents sources from being invoked twice during one compilation.
/// </summary>
public sealed record ExecutableCompilationEnrichment
{
    public ExecutableCompilationEnrichment(
        ExecutableNode rootActivity,
        IReadOnlyCollection<ExecutableDependencyClaim>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);
        RootActivity = rootActivity;
        Dependencies = Array.AsReadOnly((dependencies ?? []).ToArray());
    }

    public ExecutableNode RootActivity { get; }
    public IReadOnlyCollection<ExecutableDependencyClaim> Dependencies { get; }
}
