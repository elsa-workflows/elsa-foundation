using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The single-writer fence a checkpoint store applies inside its own atomic boundary, against the ownership state it read
/// there. Every store calls this one rule, so a stale writer is refused for the same reason and reported with the same
/// token by every provider.
/// </summary>
public static class RuntimeExecutionFenceValidator
{
    public static void EnsureCurrent(
        string workflowExecutionId,
        RuntimeExecutionFence expected,
        ExecutionLivenessState? ownershipState,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(expected);

        var currentToken = ReadHighestIssuedToken(ownershipState);
        var lease = ownershipState?.ExecutionLease;
        if (lease is null)
            throw new RuntimeStaleFencingTokenException(workflowExecutionId, expected.FencingToken, currentToken, RuntimeFencingRejectionReason.NoActiveLease);
        if (lease.IsExpired(now))
            throw new RuntimeStaleFencingTokenException(workflowExecutionId, expected.FencingToken, currentToken, RuntimeFencingRejectionReason.ExpiredLease);
        if (!StringComparer.Ordinal.Equals(lease.LeaseId, expected.LeaseId) ||
            !StringComparer.Ordinal.Equals(lease.OwnerId, expected.OwnerId) ||
            lease.FencingToken != expected.FencingToken)
            throw new RuntimeStaleFencingTokenException(workflowExecutionId, expected.FencingToken, currentToken, RuntimeFencingRejectionReason.StaleToken);
    }

    /// <summary>The ownership record's issued-token counter, falling back to the live lease for records that predate it.</summary>
    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
            return token;
        return state.ExecutionLease?.FencingToken ?? 0;
    }
}
