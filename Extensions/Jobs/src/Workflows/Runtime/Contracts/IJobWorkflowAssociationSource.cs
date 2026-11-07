using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Contracts;

/// <summary>Optional model-owned source for a Jobs native worker's actual owner and exact root consumption evidence.</summary>
/// <remarks>Exactly one source must support a model-work origin. Sources never grant permissions or acknowledge Jobs delivery.</remarks>
public interface IJobWorkflowAssociationSource
{
    bool Supports(RuntimeModelNativeWorkOrigin origin);

    /// <exception cref="JobException">The exact model owner is missing, unavailable or does not match the retained origin.</exception>
    ValueTask<JobWorkflowOwner> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default);

    /// <exception cref="JobException">The authoritative model evidence is unavailable or mismatched; absence is never terminal proof.</exception>
    ValueTask<JobWorkflowOwnerObservation> InspectAsync(JobWorkflowOwner owner, JobNativeDeliveryEvidence? evidence,
        CancellationToken cancellationToken = default);
}

/// <summary>Native evidence already checked against a final attempt; the model source additionally proves actual root consumption.</summary>
public sealed record JobNativeDeliveryEvidence(JobNativeAddress Address, RunJobCompletion Completion,
    JobResult Result, string NativeDeliveryId, string NativeAttemptId, ActivityExecutionStatus NativeStatus,
    RunJobResult? NativeResult, string? NativeFaultMarker);

/// <summary>Positive model-owner evidence; transport results or a missing root are not consumption or terminality.</summary>
public sealed record JobWorkflowOwnerObservation(JobWorkflowOwnerDisposition Disposition, string? ModelReceiptId,
    string? ResultId, string? ResultSha256);

/// <summary>The exact root's durable disposition, separate from helper-native completion.</summary>
public enum JobWorkflowOwnerDisposition
{
    NotConsumed = 0,
    AcceptedInput = 1,
    ProvenTerminal = 2
}
