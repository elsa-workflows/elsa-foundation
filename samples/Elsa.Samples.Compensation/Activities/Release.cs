using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Compensation.Ledger;

namespace Elsa.Samples.Compensation.Activities;

/// <summary>
/// Undoes a <see cref="Reserve"/> as a compensation. The scope gives it the reserving step's operation key as
/// <see cref="ActivityExecutionContext.TargetOperationKey"/>: it releases the reservation that key made, or, when there
/// is none yet, records a tombstone so the reservation is refused if it arrives late. It is done once per its own
/// operation key, which stays the same on every retry. With <see cref="FailTimes"/> its first calls fail before any
/// effect, as a briefly unavailable service would. It keeps the default <c>External</c> side-effect profile.
/// </summary>
public sealed class Release(ReservationLedger ledger) : Activity<ActivityUnit>
{
    public const string UnavailableCode = "reservation.unavailable";
    public const string MismatchCode = "reservation.mismatch";
    public const string NoTargetCode = "reservation.no-target";
    public const string InvalidResourceCode = Reserve.InvalidResourceCode;

    /// <summary>The resource whose reservation to release.</summary>
    [ActivityInput(Key = "resource")]
    [Required]
    public string Resource { get; set; } = null!;

    /// <summary>
    /// The reservation the step reported, bound to the step's result. Optional: without it the release works from the
    /// target operation key alone, which is all an uncertain step leaves. When given, a target that made a different
    /// reservation is refused.
    /// </summary>
    [ActivityInput(Key = "reservation")]
    public string? Reservation { get; set; }

    /// <summary>How many calls fail before the first one that works, counted per operation key.</summary>
    [ActivityInput(Key = "failTimes")]
    public int? FailTimes { get; set; }

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (!ReservationLedger.IsValidResource(Resource))
            return ActivityTransition.Fault<ActivityUnit>(new ActivityFault(InvalidResourceCode, $"'{Resource}' is not a valid resource name."));

        var result = await ledger.ReleaseAsync(Resource, context.OperationKey, context.TargetOperationKey, Reservation, FailTimes ?? 0,
            context.WorkflowExecutionId, context.CancellationToken);
        return result.Status switch
        {
            ReleaseStatus.Unavailable => ActivityTransition.Fault<ActivityUnit>(
                new ActivityFault(UnavailableCode, $"The {Resource} service is unavailable (attempt {result.FailedAttempts}).", isRetryable: true)),
            ReleaseStatus.Mismatch => ActivityTransition.Fault<ActivityUnit>(new ActivityFault(MismatchCode,
                $"The step reported reservation '{Reservation}', but its operation key made {(result.ReservationId is null ? "none" : $"'{result.ReservationId}'")} in {Resource}.")),
            ReleaseStatus.NoTarget => ActivityTransition.Fault<ActivityUnit>(new ActivityFault(NoTargetCode,
                "Release needs a target operation key (run it as a compensation) or a reservation.")),
            _ => ActivityTransition.Complete(ActivityUnit.Value)
        };
    }
}
