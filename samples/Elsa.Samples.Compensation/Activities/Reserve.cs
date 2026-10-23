using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Compensation.Ledger;

namespace Elsa.Samples.Compensation.Activities;

/// <summary>
/// Reserves a resource in the sample participant, once per operation key: the same key again (after a crash, say)
/// returns the original reservation. A key that a <see cref="Release"/> already released is refused, so a request that
/// arrives late cannot slip in after its undo. It calls an external system, so it keeps the default
/// <c>External</c> side-effect profile: the runtime commits before it runs.
/// </summary>
public sealed class Reserve(ReservationLedger ledger) : Activity<ReservationResult>
{
    public const string RejectedCode = "reservation.rejected";
    public const string RefusedCode = "reservation.refused";
    /// <summary>The code of the fault a lost response reports (its category is <c>outcome-unknown</c>).</summary>
    public const string TimeoutCode = "reservation.timeout";
    public const string InvalidResourceCode = "reservation.invalid-resource";

    /// <summary>The resource to reserve: one ledger file per resource.</summary>
    [ActivityInput(Key = "resource")]
    [Required]
    public string Resource { get; set; } = null!;

    /// <summary>When true, the participant rejects the request for business reasons, with no effect.</summary>
    [ActivityInput(Key = "reject")]
    public bool? Reject { get; set; }

    /// <summary>When true, the reservation is applied and then its response is lost: the step's outcome is unknown.</summary>
    [ActivityInput(Key = "loseResponse")]
    public bool? LoseResponse { get; set; }

    protected override async ValueTask<ActivityTransition<ReservationResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (!ReservationLedger.IsValidResource(Resource))
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault(InvalidResourceCode, $"'{Resource}' is not a valid resource name."));

        var result = await ledger.ReserveAsync(Resource, context.OperationKey, context.WorkflowExecutionId, Reject == true, context.CancellationToken);
        switch (result.Status)
        {
            case ReserveStatus.Rejected:
                return ActivityTransition.Fault<ReservationResult>(new ActivityFault(RejectedCode, $"The {Resource} request was rejected."));
            case ReserveStatus.Refused:
                return ActivityTransition.Fault<ReservationResult>(new ActivityFault(RefusedCode, $"The {Resource} reservation was already released; this late request is refused."));
        }

        // The reservation exists now, but the caller never hears so: report that the outcome is unknown.
        if (LoseResponse == true)
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault("reservation.timeout", $"No response from the {Resource} service.", category: "outcome-unknown"));

        return ActivityTransition.Complete(new ReservationResult(result.ReservationId!));
    }
}

/// <summary>The reservation a <see cref="Reserve"/> made, which its <see cref="Release"/> can bind to.</summary>
public sealed record ReservationResult([property: Output(Key = ReservationResult.OutputKey, Path = "reservation")] string Reservation)
{
    public const string OutputKey = "reservation";
}
