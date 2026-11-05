using System.Diagnostics;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

[Collection("Run history compound trace isolation")]
public sealed partial class DispatchWorkflowHistoryTests
{
    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Compound_failure_preserves_child_permanent_delivery_classification_and_unadvanced_source()
    {
        var primary = new RuntimeCheckpointCommitValidationException("Constructed permanent child checkpoint refusal.");
        var fault = new CompoundChildWriterProbe("ChildStartDispositionObserved");
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true, configureServices: services =>
        {
            var start = services.Last(item => item.ServiceType == typeof(IWorkflowStartDispatcher));
            services.Remove(start);
            services.AddScoped<IWorkflowStartDispatcher>(sp => new CompoundChildStartRefusal(ResolveCompoundOriginal<IWorkflowStartDispatcher>(start, sp), primary));
            DecorateCompoundWriter(services, fault);
        });
        var run = await fixture.StartParentAsync("compound-child", "compound-child-parent", "correlation", waitForCompletion: true);
        var intent = Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit), item => item.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        var id = RuntimePostCommitOutboxItems.OutboxItemId(run.CompletionCommit.CommitId, intent);

        var boundary = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () => { await fixture.SweepAsync(); });

        Assert.False(boundary.IsUnavailable); Assert.Equal(id, boundary.SourceOutboxItemId);
        Assert.Equal(DispatchWorkflowConstants.StartChildIntentKind, boundary.IntentKind);
        var compound = Assert.IsType<AggregateException>(boundary.InnerException);
        var delivery = Assert.IsType<RuntimePostCommitDeliveryException>(compound.InnerExceptions[0]);
        Assert.Equal(PostCommitFailureKind.Permanent, delivery.Kind); Assert.Same(primary, delivery.InnerException);
        var observation = Assert.IsType<RuntimePostCommitSourceBoundaryException>(compound.InnerExceptions[1]);
        Assert.True(observation.IsUnavailable); Assert.Same(fault.Cause, observation.InnerException);
        Assert.Equal(2, fault.Writes.Count); Assert.Same(fault.Writes[0], fault.Writes[1]);
        await using var scope = fixture.Services.CreateAsyncScope();
        var held = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status); Assert.Equal(0, held.DeliveryAttemptCount); Assert.Null(held.DeliveredAt);
        Assert.Empty(fixture.ChildProbe.Observations);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        var history = await scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.Single(history.Items, item => item.Name == "ChildStartAttemptObserved");
        Assert.DoesNotContain(history.Items, item => item.Name == "ChildStartDispositionObserved");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Compound_failure_translates_only_current_parent_request_diagnostics_and_preserves_reused_exception_history(bool traceFailure)
    {
        using var trace = traceFailure ? new CompoundTraceFailureScope() : null;
        Exception primary = traceFailure ? new IOException("Constructed reusable parent dependency outage.")
            : new ArgumentException("Constructed reusable parent mailbox refusal.");
        var priorDiagnostic = new IOException("Earlier unrelated diagnostic; it grants no current source authority.");
        primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey] = priorDiagnostic;
        var actor = new CompoundParentActorProbe(primary);
        var fault = new CompoundChildWriterProbe("DeliveryFaulted");
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true, configureServices: services =>
        {
            var actors = services.Last(item => item.ServiceType == typeof(IWorkflowExecutionActorProvider));
            services.Remove(actors);
            services.AddSingleton<IWorkflowExecutionActorProvider>(sp => new CompoundParentActors(ResolveCompoundOriginal<IWorkflowExecutionActorProvider>(actors, sp), actor));
            DecorateCompoundWriter(services, fault);
        });
        var run = await fixture.StartParentAsync("compound-parent", "compound-parent-owner", "correlation", waitForCompletion: true);
        await using var scope = fixture.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var claims = services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var start = Assert.Single(await claims.ClaimAsync(new("compound-child-setup", DispatchWorkflowRuntimeTestFixture.Now,
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.StartChildIntentKind)));
        await services.GetRequiredService<ChildStartExecutor>().HandleAsync(new PostCommitDeliveryContext(start, new("tenant-42")));
        Assert.Single(fixture.ChildProbe.Observations);
        var claim = Assert.Single(await claims.ClaimAsync(new("compound-parent-owner", DispatchWorkflowRuntimeTestFixture.Now,
            TimeSpan.FromMinutes(5), 1, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.ResumeParentIntentKind)));
        var source = new PostCommitDeliveryContext(claim, new("tenant-42"));
        actor.Target = run.Start.WorkflowExecutionId;
        var handler = services.GetRequiredService<ParentResumeExecutor>();

        var first = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => handler.HandleAsync(source).AsTask());

        Assert.Equal(traceFailure, first.IsUnavailable); Assert.Equal(source.SourceOutboxItemId, first.SourceOutboxItemId);
        Assert.Equal(source.Intent.Kind, first.IntentKind);
        var compound = Assert.IsType<AggregateException>(first.InnerException);
        Assert.Equal(2, compound.InnerExceptions.Count);
        Assert.Same(primary, compound.InnerExceptions[0]);
        Assert.Same(fault.Cause, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(compound.InnerExceptions[1]).InnerException);
        var old = Assert.IsAssignableFrom<IReadOnlyList<object>>(primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        Assert.Equal(traceFailure ? 3 : 2, old.Count); Assert.Same(priorDiagnostic, old[0]);
        if (trace is not null)
        {
            Assert.Same(trace.Failure, old[2]);
            Assert.DoesNotContain(compound.InnerExceptions, cause => ReferenceEquals(cause, trace.Failure));
        }

        // Same exception object, same verified source, NEW request: its failure observation now succeeds.
        // A stale diagnostic must not promote this call back into a current compound/source-boundary failure.
        fault.Enabled = false;
        Assert.Same(primary, await Record.ExceptionAsync(() => handler.HandleAsync(source).AsTask()));
        Assert.Same(old, primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        fault.Enabled = true;
        var third = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => handler.HandleAsync(source).AsTask());
        Assert.Equal(traceFailure, third.IsUnavailable);
        var thirdCauses = Assert.IsType<AggregateException>(third.InnerException).InnerExceptions;
        Assert.Equal(2, thirdCauses.Count); Assert.Same(primary, thirdCauses[0]);
        var all = Assert.IsAssignableFrom<IReadOnlyList<object>>(primary.Data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey]);
        Assert.Equal(traceFailure ? 5 : 3, all.Count); Assert.Same(old[0], all[0]); Assert.Same(old[1], all[1]);
        if (trace is not null)
        {
            Assert.Same(trace.Failure, all[2]); Assert.Same(trace.Failure, all[4]);
            Assert.Equal(2, trace.Writes);
        }
        Assert.Equal(3, actor.Refusals);
        var held = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(source.SourceOutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status); Assert.Equal(0, held.DeliveryAttemptCount); Assert.Null(held.DeliveredAt);
        Assert.Single(fixture.ChildProbe.Observations);
        var parent = await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, parent!.Status);
    }

    private static T ResolveCompoundOriginal<T>(ServiceDescriptor descriptor, IServiceProvider services) where T : class =>
        (T)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(services) ??
            ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!));

    private static void DecorateCompoundWriter(IServiceCollection services, CompoundChildWriterProbe fault)
    {
        var writer = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
        services.Remove(writer);
        services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new CompoundChildWriter(
            ResolveCompoundOriginal<IRuntimeHistoryObservationWriter>(writer, sp), fault));
    }

    private sealed class CompoundChildWriterProbe(string kind)
    {
        public string Kind { get; } = kind;
        public bool Enabled = true;
        public Exception Cause { get; } = new IOException("Constructed failure-observation write outage.");
        public List<RuntimeHistoryObservation> Writes { get; } = [];
    }
    private sealed class CompoundChildWriter(IRuntimeHistoryObservationWriter inner, CompoundChildWriterProbe fault) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            if (fault.Enabled && observation.Event.Name == fault.Kind)
            { fault.Writes.Add(observation); return ValueTask.FromException<RuntimeHistoryObservationResult>(fault.Cause); }
            return inner.RecordAsync(observation, cancellationToken);
        }
    }
    private sealed class CompoundChildStartRefusal(IWorkflowStartDispatcher inner, Exception failure) : IWorkflowStartDispatcher
    {
        public bool CanDispatchSavedStimulus(IStimulusDeliveryStore sources, IWorkflowExecutableStore material, IWorkflowExecutionStateStore executions) =>
            inner.CanDispatchSavedStimulus(sources, material, executions);
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default) =>
            request.ParentWorkflowExecutionId is not null ? ValueTask.FromException<WorkflowExecutionStartDispatchResult>(failure)
                : inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
    }
    private sealed class CompoundParentActorProbe(Exception failure)
    { public Exception Failure { get; } = failure; public string? Target; public int Refusals; }
    private sealed class CompoundParentActors(IWorkflowExecutionActorProvider inner, CompoundParentActorProbe fault) : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => inner.Capabilities;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.WorkflowExecutionId == fault.Target && request.Reason == WorkflowExecutionActorActivationReason.ResumeBookmark)
            { fault.Refusals++; return ValueTask.FromException<IWorkflowExecutionActor>(fault.Failure); }
            return inner.GetAgentAsync(request, cancellationToken);
        }
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => inner.PassivateAsync(request, cancellationToken);
    }

    private sealed class CompoundTraceFailureScope : IDisposable
    {
        private readonly CompoundThrowingTraceListener listener = new();
        internal Exception Failure => listener.Failure;
        internal int Writes => listener.Writes;
        internal CompoundTraceFailureScope() => Trace.Listeners.Add(listener);
        public void Dispose() { Trace.Listeners.Remove(listener); listener.Dispose(); }
    }
    private sealed class CompoundThrowingTraceListener : TraceListener
    {
        internal Exception Failure { get; } = new InvalidOperationException("Constructed supplementary trace-listener failure.");
        internal int Writes { get; private set; }
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
            TraceEvent(eventCache, source, eventType, id, format);
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (message != "A delivery failure observation also failed; its unchanged primary exception retains the supplementary cause.") return;
            Writes++;
            throw Failure;
        }
    }
}

// A listener is process-global. xUnit runs this nonparallel collection apart from every parallel collection;
// the owned scope still removes only its own listener in finally, including a failing assertion.
[CollectionDefinition("Run history compound trace isolation", DisableParallelization = true)]
public sealed class RunHistoryCompoundTraceIsolation { }
