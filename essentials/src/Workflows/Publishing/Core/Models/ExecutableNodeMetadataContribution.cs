using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>The immutable compilation context visible to executable-node metadata sources.</summary>
public sealed record ExecutableNodeMetadataContext(
    WorkflowExecutableCompileRequest Request,
    WorkflowExecutableCompileSource Source,
    ExecutableNode RootActivity);

/// <summary>One metadata key/value claimed for one compiled executable node.</summary>
public sealed record ExecutableNodeMetadataContribution(
    string ExecutableNodeId,
    string Key,
    string Value)
{
    /// <summary>Runtime-owned identity stamped by the single aggregating event handler.</summary>
    public string? SourceIdentity { get; init; }
}
