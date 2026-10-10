using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

// The canary activities live outside the test project's namespace on purpose: their full type name is the activity type
// key the catalog, the runtime and every inspection surface record, and no part of it may match a name-based redactor
// (spec 188, T078, research R10). "Secrets", in the project's namespace, would.
namespace Elsa.Canary.Fixtures;

/// <summary>What a canary activity does, chosen by the literal bound to its <see cref="CanaryActivity.Companion"/> input.</summary>
public static class CanaryModes
{
    /// <summary>Records what it was hydrated with and completes.</summary>
    public const string Record = "record";

    /// <summary>Throws an exception whose message holds the value it was hydrated with.</summary>
    public const string Throw = "throw";

    /// <summary>Returns an <see cref="ActivityFault"/> whose message holds the value it was hydrated with.</summary>
    public const string Fault = "fault";

    /// <summary>Suspends, and completes when it is resumed.</summary>
    public const string Suspend = "suspend";

    /// <summary>Suspends, and throws an exception whose message holds the value it was hydrated with when it is resumed.</summary>
    public const string ThrowOnResume = "resume-throw";
}

public sealed record CanaryWaitState(string Mode);

public sealed record CanaryWaitTrigger(bool Released);

/// <summary>
/// The canary activity (spec 188, T078). <see cref="Primary"/> is declared a credential, <see cref="Companion"/> is
/// undeclared and bound to a non-secret literal that selects the <see cref="CanaryModes">mode</see>, and
/// <see cref="Plain"/> is undeclared, so its pinned contract policy is not sensitive (scenario S10).
/// </summary>
/// <remarks>
/// <para>
/// It is declared ReplaySafe, which it is (it records in memory and throws, suspends or completes the same way each
/// time), so that under coalesced checkpoint persistence the runtime fuses its schedule, start and invoke stages
/// (<see cref="Elsa.Secrets.Workflows.Tests.Support.CanaryStartMode.Fused"/>); under immediate persistence it runs each
/// stage as its own dispatch, as any other activity does.
/// </para>
/// <para>
/// It hands what it was hydrated with to <see cref="CanaryRecorder"/>, in memory, and puts the value it holds into
/// the text of the exception it throws or the fault it returns in those modes, which is the text the runtime's masking
/// is tested on. It does not log the value, return it in its result or store it in its private state or a bookmark
/// payload, which phase 0 does not cover (research R9, limits).
/// </para>
/// </remarks>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class CanaryActivity(CanaryRecorder recorder) : StatefulActivity<ActivityUnit, CanaryWaitState, CanaryWaitTrigger>
{
    public const string ResumeTargetId = "canary-wait";
    public const string StimulusType = "CanaryWait";
    public const string FaultCode = "CanaryFault";

    [ActivityInput(IsCredential = true)]
    public string? Primary { get; set; }

    [ActivityInput]
    public string? Companion { get; set; }

    [ActivityInput]
    public string? Plain { get; set; }

    /// <summary>The stimulus hash that resumes the suspended canary activity of <paramref name="workflowExecutionId"/>, and only it.</summary>
    public static string StimulusHash(string workflowExecutionId) => $"canary-wait:{workflowExecutionId}";

    /// <summary>The failure text the activity reports in the throwing and faulting modes.</summary>
    public static string FailureText(string? held) => $"The canary activity failed while it held {held}.";

    protected override ValueTask<ActivityTransition<ActivityUnit, CanaryWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        recorder.Add(context.WorkflowExecutionId, CanaryRecorder.Execute, Primary, Plain);
        return ValueTask.FromResult(Companion switch
        {
            CanaryModes.Record => Complete(ActivityUnit.Value),
            CanaryModes.Throw => throw new CanaryFailureException(FailureText(Held)),
            CanaryModes.Fault => Fault(new ActivityFault(FaultCode, FailureText(Held))),
            CanaryModes.Suspend or CanaryModes.ThrowOnResume => Suspend(
                new CanaryWaitState(Companion),
                [new ActivityTriggerRegistration<CanaryWaitTrigger>(ResumeTargetId, StimulusType, StimulusHash(context.WorkflowExecutionId))]),
            _ => throw new InvalidOperationException("The canary activity was bound to a mode it does not know.")
        });
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<ActivityUnit, CanaryWaitState>> ResumeAsync(ActivityResumeContext<CanaryWaitState, CanaryWaitTrigger> context)
    {
        recorder.Add(context.WorkflowExecutionId, CanaryRecorder.Resume, Primary, Plain);
        return context.State.Mode == CanaryModes.ThrowOnResume
            ? throw new CanaryFailureException(FailureText(Held))
            : ValueTask.FromResult(Complete(ActivityUnit.Value));
    }

    private string? Held => Primary ?? Plain;
}

/// <summary>What a canary activity throws: its message holds the value the activity was hydrated with.</summary>
public sealed class CanaryFailureException(string message) : Exception(message);

/// <summary>
/// The canary's checkpoint participant (scenario S7): an activity that takes part in its own checkpoints, on whose input
/// publish refuses a secret reference (<c>VF-ACT-012</c>), because a participant reads its inputs outside activation.
/// It never runs.
/// </summary>
public sealed class CanaryCheckpointActivity : Activity<ActivityUnit>, IRuntimeActivityCheckpointParticipant
{
    [ActivityInput]
    public string? Text { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        throw new NotSupportedException("The canary's checkpoint participant is only ever refused at publish.");

    public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The canary's checkpoint participant is only ever refused at publish.");

    public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyCollection<DurableValueState> persistedValues,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The canary's checkpoint participant is only ever refused at publish.");
}
