using System.Text;
using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Shared arrangements for activation-time secret resolution (spec 188, slice 3): a secret read as publish compiles it,
/// the withheld envelope it leaves in the snapshot, a resolver double, and activities that record the value their
/// secret-bound input was hydrated with.
/// </summary>
internal static class SecretResolutionTestSupport
{
    public const string InputKey = "token";
    public const string ReferenceName = "payments.api-key";

    public static readonly ValueTypeDescriptor StringType = new("String");

    /// <summary>The plan publish pins for a secret read on a <c>String</c> input: identity from text.</summary>
    public static readonly ValueConversionPlan TextPlan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue);

    public static RuntimeSecretReference Reference(string name = ReferenceName) => new(name, "text");

    /// <summary>A secret read on <paramref name="inputKey"/> as publish compiles it.</summary>
    public static RuntimeInputBinding SecretRead(string inputKey = InputKey, string referenceName = ReferenceName) =>
        new(
            inputKey,
            StringType,
            SecretBindingTestSupport.SecretPolicy,
            RuntimeInputBindingSource.SecretRead,
            conversionPlan: TextPlan,
            secret: Reference(referenceName));

    /// <summary>The withheld envelope a secret read leaves in the committed input snapshot.</summary>
    public static ValueEnvelope Withheld(string referenceName = ReferenceName) =>
        ValueEnvelope.Withheld(StringType, WithheldValue.SecretReference(Reference(referenceName), TextPlan), SecretBindingTestSupport.SecretPolicy);

    /// <summary>
    /// An activator's secret input collaborator composed as a host without a resolver composes it: a withheld secret is
    /// refused with the missing-resolver activation failure. A test that observes a collaborator passes its own.
    /// </summary>
    public static ActivitySecretInputResolver NoResolverSecretInputResolver(
        IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IRuntimeValueConversionExecutor? valueConversionExecutor = null) =>
        new(
            partitionAccessor ?? new CountingPartitionAccessor(WorkflowExecutionPartition.DefaultValue),
            workflowExecutionStateStore ?? new SingleInstanceStateStore(null),
            valueConversionExecutor ?? new RuntimeValueConversionExecutor(),
            new DefaultRuntimeSecretMask());

    /// <summary>
    /// Arms <paramref name="resolver"/> so that its next resolution cancels the returned source and observes the
    /// cancellation, as an activation canceled while it resolves a secret does. The test hands the source's token to
    /// the work handler under test.
    /// </summary>
    public static CancellationTokenSource CancelOnNextResolution(FakeRuntimeSecretResolver resolver)
    {
        var cancellation = new CancellationTokenSource();
        resolver.Respond = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return RuntimeSecretResolution.Failure("StoreUnavailable", isRetryable: true);
        };
        return cancellation;
    }

    /// <summary>
    /// Asserts a work handler let the activation's cancellation for <paramref name="cancellationToken"/> escape with the
    /// failure of disposing the activation lease, under <paramref name="message"/>, instead of recording a fault.
    /// </summary>
    public static void AssertCanceledWithDisposalFailure(
        AggregateException exception,
        string message,
        CancellationToken cancellationToken,
        string disposalMessage)
    {
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.Equal(cancellationToken, Assert.IsAssignableFrom<OperationCanceledException>(inner).CancellationToken),
            inner => Assert.Equal(disposalMessage, inner.Message));
    }

    /// <summary>
    /// A harness whose host composes <paramref name="resolver"/> and the recorder the test activities write to. The
    /// workflow runs under the default partition, and its instance records no tenant.
    /// </summary>
    public static WorkflowExecutionHarness NewHarness(
        FakeRuntimeSecretResolver resolver,
        SecretValueRecorder recorder,
        IReadOnlyCollection<string> activityExecutionIds,
        Action<IServiceCollection>? configure = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRuntimeSecretResolver>(resolver);
                services.AddSingleton(resolver);
                services.AddSingleton(recorder);
                configure?.Invoke(services);
            })
            .Build(activityExecutionIds);

    /// <summary>A CLR node of <paramref name="activityType"/> whose input <see cref="InputKey"/> is bound to <see cref="ReferenceName"/>.</summary>
    public static ExecutableNode NewSecretNode(string nodeId, Type activityType, params ExecutableNode[] children) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/secret",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "secret" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [InputKey] = SecretRead() },
            metadata: new Dictionary<string, string>(),
            childSlots: children.Length == 0 ? null : [new ExecutableChildSlot(StructuralExecutionTestSupport.ChildSlotName, children)]);

    /// <summary>The resume target of a <see cref="SecretWaitingActivity"/> on <paramref name="nodeId"/>.</summary>
    public static string WaitResumeTargetId(string nodeId) =>
        WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, SecretWaitingActivity.ResumeTargetKey);

    /// <summary>
    /// A workflow whose root is a <see cref="SecretWaitingActivity"/>, or another activity of
    /// <paramref name="activityType"/> that suspends on its resume target, on <paramref name="nodeId"/>, with that resume
    /// target.
    /// </summary>
    public static WorkflowExecutable NewWaitingExecutable(string nodeId, Type? activityType = null)
    {
        var resumeTargetId = WaitResumeTargetId(nodeId);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: NewSecretNode(nodeId, activityType ?? typeof(SecretWaitingActivity)),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, nodeId, "ResumeAsync", new Dictionary<string, string>(), SecretWaitingActivity.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    /// <summary>Asserts the committed snapshot of <paramref name="state"/> holds the withheld reference and no value.</summary>
    public static void AssertSnapshotWithheld(ActivityExecutionState state, string referenceName = ReferenceName)
    {
        var envelope = state.InputSnapshot!.Values[InputKey];
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Null(envelope.InlineValue);
        Assert.Null(envelope.ExternalReference);
        Assert.Equal(WithheldValueKind.SecretReference, envelope.WithheldValue!.Kind);
        Assert.Equal(referenceName, envelope.WithheldValue.Secret!.Name);
    }

    /// <summary>
    /// Asserts no persisted activity state and no inspection projection of the run contains any of
    /// <paramref name="values"/>. Each must have reached an activity, so the scan has something to miss.
    /// </summary>
    public static async Task AssertNotPersistedAsync(WorkflowExecutionHarness harness, IReadOnlyCollection<string?> values)
    {
        var states = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId);
        var inspectionStore = harness.Services.GetRequiredService<IActivityExecutionInspectionStore>();
        var persisted = new StringBuilder(JsonSerializer.Serialize(states));
        foreach (var state in states)
            persisted.Append(JsonSerializer.Serialize(await inspectionStore.FindAsync(harness.ExecutionId, state.Execution.ActivityExecutionId)));
        var persistedText = persisted.ToString();
        Assert.NotEmpty(states);
        foreach (var value in values)
            Assert.DoesNotContain(Assert.IsType<string>(value), persistedText, StringComparison.Ordinal);
    }
}

/// <summary>Reports one partition and counts its reads.</summary>
internal sealed class CountingPartitionAccessor(string partition) : IWorkflowExecutionPartitionAccessor
{
    public int Reads { get; private set; }

    public WorkflowExecutionPartition Current
    {
        get
        {
            Reads++;
            return new(partition);
        }
    }
}

/// <summary>Holds one workflow instance and counts its reads; nothing else is read or written.</summary>
internal sealed class SingleInstanceStateStore(WorkflowExecutionState? instance) : IWorkflowExecutionStateStore
{
    public WorkflowExecutionState? Instance { get; set; } = instance;

    public int Reads { get; private set; }

    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        Reads++;
        return ValueTask.FromResult(Instance is { } state && state.WorkflowExecutionId == workflowExecutionId ? state : null);
    }

    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// A runtime secret resolver double that records every request. By default each reference resolves to
/// <see cref="ValueOf"/> its name; a test replaces <see cref="Respond"/> to rotate a value or to fail.
/// </summary>
public sealed class FakeRuntimeSecretResolver : IRuntimeSecretResolver
{
    private readonly List<RuntimeSecretResolutionRequest> _requests = [];

    public Func<RuntimeSecretResolutionRequest, CancellationToken, RuntimeSecretResolution> Respond { get; set; } =
        (request, _) => RuntimeSecretResolution.Success(ValueOf(request.Reference.Name));

    public IReadOnlyList<RuntimeSecretResolutionRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    public static string ValueOf(string referenceName) => $"value-of-{referenceName}";

    /// <summary>Answers the <c>n</c>th request (from 1) with <see cref="SequenceValue"/> of <c>n</c>, so each resolution is told apart.</summary>
    public void RespondWithCallSequence() =>
        Respond = (_, _) => RuntimeSecretResolution.Success(SequenceValue(Requests.Count));

    public static string SequenceValue(int call) => $"resolved-secret-{call}";

    public ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_requests)
            _requests.Add(request);
        return ValueTask.FromResult(Respond(request, cancellationToken));
    }
}

/// <summary>
/// Collects the values the test activities were hydrated with, in the order they ran, each with the step that read it:
/// <see cref="Execute"/>, <see cref="Resume"/>, <see cref="ChildNotified"/> or <see cref="ChildCompleted"/>. Once
/// <see cref="DisposalFailure"/> is set, the test activities throw it when their activation lease disposes them.
/// </summary>
public sealed class SecretValueRecorder
{
    public const string Execute = "execute";
    public const string Resume = "resume";
    public const string ChildNotified = "child-notified";
    public const string ChildCompleted = "child-completed";

    private readonly List<(string Step, string? Value)> _entries = [];

    public IReadOnlyList<(string Step, string? Value)> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public IReadOnlyList<string?> Values => Entries.Select(entry => entry.Value).ToArray();

    public Exception? DisposalFailure { get; set; }

    public void ThrowIfDisposalFails()
    {
        if (DisposalFailure is { } failure)
            throw failure;
    }

    public void Add(string step, string? value)
    {
        lock (_entries)
            _entries.Add((step, value));
    }
}

/// <summary>A leaf that records its secret-bound input, suspends, and records the input again when it is resumed.</summary>
public sealed class SecretWaitingActivity(SecretValueRecorder recorder) : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>, IDisposable
{
    public const string ResumeTargetKey = "wait";
    public const string StimulusType = "TestSecretWait";
    public const string StimulusHash = "secret-wait:1";

    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        recorder.Add(SecretValueRecorder.Execute, Token);
        return ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, StimulusType, StimulusHash)]));
    }

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context)
    {
        recorder.Add(SecretValueRecorder.Resume, Token);
        return ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }

    public void Dispose() => recorder.ThrowIfDisposalFails();
}

/// <summary>
/// A structural parent with a secret-bound input that records the input on its initial execution and on every
/// evaluation: a child notification (which it defers) and its child's completion (which completes it).
/// </summary>
public sealed class SecretReadingParentActivity(SecretValueRecorder recorder) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildNotificationHandler,
    IDisposable
{
    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        recorder.Add(SecretValueRecorder.Execute, Token);
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        recorder.Add(SecretValueRecorder.ChildNotified, Token);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        recorder.Add(SecretValueRecorder.ChildCompleted, Token);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    public void Dispose() => recorder.ThrowIfDisposalFails();
}

/// <summary>
/// A structural parent that opts into re-materializing its inputs when its child completes (as <c>Do</c> and
/// <c>While</c> do). It records its secret-bound input on its initial execution and in the child-completion
/// evaluation, which completes it.
/// </summary>
public sealed class SecretRematerializingParentActivity(SecretValueRecorder recorder) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeRematerializeInputsOnChildCompletion,
    IDisposable
{
    [ActivityInput(Key = SecretResolutionTestSupport.InputKey)]
    public string Token { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        recorder.Add(SecretValueRecorder.Execute, Token);
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        recorder.Add(SecretValueRecorder.ChildCompleted, Token);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    public void Dispose() => recorder.ThrowIfDisposalFails();
}
