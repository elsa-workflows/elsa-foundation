using Elsa.Cluster.Core.Options;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Distributed.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// Refuses a <c>WorkflowsRuntimeDistributed:NodeId</c> setting that differs from the member's host id (spec 184, FR-003).
/// The routing identity is the host id (FR-001); the setting is no longer a source of identity, and a value that names a
/// different id would otherwise be accepted silently.
/// </summary>
public sealed class DistributedRuntimeNodeIdValidator(string? configuredNodeId) : IValidateOptions<ExecutionPlacementOptions>
{
    public ValidateOptionsResult Validate(string? name, ExecutionPlacementOptions options)
    {
        if (string.IsNullOrWhiteSpace(configuredNodeId) || string.Equals(configuredNodeId, options.NodeId, StringComparison.Ordinal))
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            $"WorkflowsRuntimeDistributed:NodeId is set to '{configuredNodeId}', but the distributed runtime's routing identity is " +
            $"this member's cluster host id, '{options.NodeId}'. The setting is no longer a source of identity: remove it, or " +
            $"set the host id with {ClusterMembershipOptions.SectionName}:{nameof(ClusterMembershipOptions.HostId)} on the host.");
    }
}

/// <summary>
/// Materializes the placement options while the shell starts, so a refused <c>NodeId</c> setting fails the shell's
/// activation instead of its first placement decision (spec 184, FR-003).
/// </summary>
public sealed class ValidateDistributedRuntimeIdentityStartupTask(IOptions<ExecutionPlacementOptions> options) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = options.Value;
        return Task.CompletedTask;
    }
}
