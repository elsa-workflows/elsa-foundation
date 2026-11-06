namespace Elsa.Jobs.Core;

/// <summary>The retained exact acknowledgement binding, independently of whether a continuation permission was needed.</summary>
public sealed record JobDeliveryAcknowledgement(string ContinuationId, JobDeliveryProof Proof);

/// <summary>Exact retained delivery responsibility. Absence of a record is never proof of model consumption.</summary>
public sealed record JobDeliveryStatusSnapshot(
    string JobId,
    string ResultId,
    JobAssociation Association,
    JobContinuationPermission? Permission,
    JobDeliveryAcknowledgement? Acknowledgement)
{
    public bool IsAcknowledged => Acknowledgement is not null;
}

/// <summary>Protected sender state. A cancelled, non-retired record without acceptance is the provider's pre-admission tombstone; a missing record proves nothing.</summary>
public sealed record JobOriginSnapshot(
    JobOrigin Origin,
    string RequestSha256,
    bool Cancelled,
    bool Retired,
    JobAcceptance? Acceptance);
