using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StatefulActivityContractTests
{
    [Fact]
    public async Task Initial_and_resume_attempts_use_typed_state_trigger_and_fresh_activity_objects()
    {
        var initialActivity = new AwaitApprovalActivity("activation-1");
        var initialContext = Context("attempt-1");

        var initialTransition = await AsStateful(initialActivity).ExecuteAsync(initialContext);
        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<ApprovalState>>(initialTransition);
        var registration = Assert.IsType<ActivityTriggerRegistration<ApprovalReceived>>(Assert.Single(suspension.Registrations));

        Assert.Equal(new ApprovalState("request-42"), suspension.State);
        Assert.Equal(typeof(ApprovalReceived), suspension.TriggerType);
        Assert.Equal("Elsa.Activities.Runtime.Tests.StatefulActivityContractTests+ApprovalReceived", registration.PayloadType.Alias);
        Assert.Equal(1, suspension.StateValueType.SchemaVersion);
        Assert.Equal(1, registration.PayloadType.SchemaVersion);
        Assert.Equal("approval", registration.ResumeTargetKey);

        var reusedActivationContext = new ActivityResumeContext<ApprovalState, ApprovalReceived>(
            Context("attempt-2"),
            suspension.State,
            new ApprovalReceived(true),
            registrationId: "registration-1",
            deliveryId: "delivery-1");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AsStateful(initialActivity).ResumeAsync(reusedActivationContext));

        var resumedActivity = new AwaitApprovalActivity("activation-2");
        var resumeContext = new ActivityResumeContext<ApprovalState, ApprovalReceived>(
            Context("attempt-2"),
            suspension.State,
            new ApprovalReceived(true),
            registrationId: "registration-1",
            deliveryId: "delivery-1");

        var resumedTransition = await AsStateful(resumedActivity).ResumeAsync(resumeContext);
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<ApprovalResult>>(resumedTransition);

        Assert.NotSame(initialActivity, resumedActivity);
        Assert.Equal("activation-1", initialActivity.ActivationMarker);
        Assert.Equal("activation-2", resumedActivity.ActivationMarker);
        Assert.Equal("attempt-2", resumedActivity.ObservedResumeContext!.AttemptId);
        Assert.Equal(new ApprovalResult(true), completion.Result);
        Assert.Equal("Approved", completion.Outcome);
    }

    [Fact]
    public void Initial_and_resume_contexts_are_immutable_identity_only_value_contexts()
    {
        AssertRestrictedContext(typeof(ActivityExecutionContext));
        AssertRestrictedContext(typeof(ActivityResumeContext<ApprovalState, ApprovalReceived>));
    }

    [Fact]
    public void Suspension_rejects_nonpersistable_state_and_trigger_shapes()
    {
        var registration = new ActivityTriggerRegistration<ApprovalReceived>("approval", "Approval", "approval:42");

        var stateError = Assert.Throws<ArgumentException>(() =>
            ActivityTransition<ApprovalResult, UnsafeState>.Suspend(
                new UnsafeState(() => { }),
                [registration]));
        Assert.Contains("persistable", stateError.Message, StringComparison.OrdinalIgnoreCase);

        var triggerError = Assert.Throws<ArgumentException>(() =>
            new ActivityTriggerRegistration<UnsafeTrigger>("approval", "Approval", "approval:42"));
        Assert.Contains("persistable", triggerError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stateful_transition_requires_registrations_of_the_declared_trigger_type()
    {
        var activity = new WrongTriggerActivity();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ((IStatefulActivity<ApprovalResult, ApprovalState, ApprovalReceived>)activity).ExecuteAsync(Context("attempt-1")));
    }

    private static IStatefulActivity<ApprovalResult, ApprovalState, ApprovalReceived> AsStateful(AwaitApprovalActivity activity) => activity;

    private static ActivityExecutionContext Context(string attemptId) =>
        new("workflow-1", "invocation-1", attemptId, "node-1", CancellationToken.None);

    private static void AssertRestrictedContext(Type contextType)
    {
        var properties = contextType.GetProperties();

        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(properties, property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(properties, property => typeof(IActivityExecutionContext).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(properties, property => property.PropertyType.Name.Contains("MemoryBlock", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, property => property.PropertyType.Name.Contains("VariableWriter", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(object));
        Assert.DoesNotContain(properties, property =>
            property.PropertyType.IsGenericType &&
            property.PropertyType.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }

    private sealed class AwaitApprovalActivity(string activationMarker) :
        StatefulActivity<ApprovalResult, ApprovalState, ApprovalReceived>
    {
        public string ActivationMarker { get; } = activationMarker;
        public ActivityResumeContext<ApprovalState, ApprovalReceived>? ObservedResumeContext { get; private set; }

        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(Suspend(
                new ApprovalState("request-42"),
                [new ActivityTriggerRegistration<ApprovalReceived>("approval", "Approval", "approval:42")]));

        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalReceived> context)
        {
            ObservedResumeContext = context;
            return ValueTask.FromResult(Complete(
                new ApprovalResult(context.Trigger.Approved),
                context.Trigger.Approved ? "Approved" : "Rejected"));
        }
    }

    private sealed class WrongTriggerActivity : StatefulActivity<ApprovalResult, ApprovalState, ApprovalReceived>
    {
        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition<ApprovalResult, ApprovalState>.Suspend(
                new ApprovalState("request-42"),
                [new ActivityTriggerRegistration<WrongTrigger>("wrong", "Wrong", "wrong:42")]));

        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalReceived> context) =>
            throw new NotSupportedException();
    }

    private sealed record ApprovalState(string RequestId);
    private sealed record ApprovalReceived(bool Approved);
    private sealed record WrongTrigger(string Value);
    private sealed record UnsafeState(Action Callback);
    private sealed record UnsafeTrigger(IServiceProvider Services);
    private sealed record ApprovalResult(bool Approved);
}
