using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CausalActivityResultResolverTests
{
    private const string WorkflowExecutionId = "workflow-1";
    private const string ProducerNodeId = "node-producer";
    private const string ProjectionKey = "customer-id";
    private const string RootScopeId = "scope:root";
    private readonly CausalActivityResultResolver _resolver = new();

    [Fact]
    public void Resolve_SelectsCausalProducerInsteadOfGloballyLatestCompletion()
    {
        var producer = Completed("producer-causal", ProducerNodeId, 1, RootScopeId);
        var bridge = Running("bridge", "node-bridge", 2, RootScopeId, schedulingInvocationId: producer.InvocationId);
        var consumer = Running("consumer", "node-consumer", 3, RootScopeId, schedulingInvocationId: bridge.InvocationId);
        var globallyLatest = Completed("producer-unrelated-latest", ProducerNodeId, 99, RootScopeId);

        var resolution = _resolver.Resolve(Reference(), consumer, [producer, bridge, consumer, globallyLatest]);

        Assert.NotNull(resolution);
        Assert.Equal(producer.InvocationId, resolution.ProducerInvocation.InvocationId);
        Assert.Equal(producer.Completion, resolution.Completion);
        Assert.Equal(ProjectionKey, resolution.ProjectionKey);
    }

    [Fact]
    public void Resolve_UsesProducerStructuralFrameWithinCausalLineage()
    {
        var otherFrame = Completed("producer-other-frame", ProducerNodeId, 1, "scope:other");
        var expected = Completed("producer-root-frame", ProducerNodeId, 2, RootScopeId, schedulingInvocationId: otherFrame.InvocationId);
        var consumer = Running("consumer", "node-consumer", 3, RootScopeId, schedulingInvocationId: expected.InvocationId);

        var resolution = _resolver.Resolve(Reference(), consumer, [otherFrame, expected, consumer]);

        Assert.NotNull(resolution);
        Assert.Equal(expected.InvocationId, resolution.ProducerInvocation.InvocationId);
    }

    [Fact]
    public void Resolve_FailsWhenCompletedProducerIsOutsideConsumerCausalLineage()
    {
        var unrelated = Completed("producer-unrelated", ProducerNodeId, 1, RootScopeId);
        var consumer = Running("consumer", "node-consumer", 2, RootScopeId);

        var exception = Assert.Throws<CausalActivityResultResolutionException>(
            () => _resolver.Resolve(Reference(), consumer, [unrelated, consumer]));

        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, exception.Reason);
        Assert.Equal(consumer.InvocationId, exception.ConsumerInvocationId);
        Assert.Equal(ProducerNodeId, exception.ProducerExecutableNodeId);
        Assert.Equal(ProjectionKey, exception.ProjectionKey);
        Assert.Equal(RootScopeId, exception.ProducerScopeId);
        Assert.Empty(exception.CandidateInvocationIds);
    }

    [Fact]
    public void Resolve_FailsOnMultipleCausalProducersInsteadOfChoosingNewest()
    {
        var first = Completed("producer-first", ProducerNodeId, 1, RootScopeId);
        var second = Completed("producer-second", ProducerNodeId, 2, RootScopeId, schedulingInvocationId: first.InvocationId);
        var consumer = Running("consumer", "node-consumer", 3, RootScopeId, schedulingInvocationId: second.InvocationId);

        var exception = Assert.Throws<CausalActivityResultResolutionException>(
            () => _resolver.Resolve(Reference(), consumer, [first, second, consumer]));

        Assert.Equal(CausalActivityResultResolutionFailureReason.Ambiguous, exception.Reason);
        Assert.Equal(new[] { first.InvocationId, second.InvocationId }, exception.CandidateInvocationIds);
    }

    [Fact]
    public void Resolve_RequiresCommittedCompletion()
    {
        var runningProducer = Running("producer-running", ProducerNodeId, 1, RootScopeId);
        var consumer = Running("consumer", "node-consumer", 2, RootScopeId, schedulingInvocationId: runningProducer.InvocationId);

        var exception = Assert.Throws<CausalActivityResultResolutionException>(
            () => _resolver.Resolve(Reference(), consumer, [runningProducer, consumer]));

        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, exception.Reason);
    }

    [Fact]
    public void Resolve_DoesNotCrossParallelBranchFrame()
    {
        var producer = Completed("producer-branch-a", ProducerNodeId, 1, RootScopeId, branchId: "branch-a");
        var consumer = Running("consumer-branch-b", "node-consumer", 2, RootScopeId, producer.InvocationId, branchId: "branch-b");

        var exception = Assert.Throws<CausalActivityResultResolutionException>(
            () => _resolver.Resolve(Reference(), consumer, [producer, consumer]));

        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, exception.Reason);
    }

    [Fact]
    public void Resolve_DoesNotCrossIterationFrame()
    {
        var producer = Completed("producer-iteration-1", ProducerNodeId, 1, RootScopeId, iterationId: "iteration-1");
        var consumer = Running("consumer-iteration-2", "node-consumer", 2, RootScopeId, producer.InvocationId, iterationId: "iteration-2");

        var exception = Assert.Throws<CausalActivityResultResolutionException>(
            () => _resolver.Resolve(Reference(), consumer, [producer, consumer]));

        Assert.Equal(CausalActivityResultResolutionFailureReason.Unavailable, exception.Reason);
    }

    [Fact]
    public void Resolve_AllowsScopeLessSequentialProvenanceWithinSameCausalFrame()
    {
        var producer = Completed("producer-sequence", ProducerNodeId, 1, scopeId: null);
        var consumer = Running("consumer-sequence", "node-consumer", 2, scopeId: null, producer.InvocationId);

        var resolution = _resolver.Resolve(Reference(), consumer, [producer, consumer]);

        Assert.NotNull(resolution);
        Assert.Equal(producer.InvocationId, resolution.ProducerInvocation.InvocationId);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenOptionalProducerIsUnavailable()
    {
        var consumer = Running("consumer", "node-consumer", 1, RootScopeId);

        var resolution = _resolver.Resolve(Reference(isOptional: true), consumer, [consumer]);

        Assert.Null(resolution);
    }

    [Fact]
    public void Resolve_PortableExpressionBinding_UsesPublicationValidatedScopeAndCausalFrame()
    {
        var producer = Completed("producer-nested", ProducerNodeId, 1, "scope:nested", branchId: "branch-a");
        var consumer = Running("consumer", "node-consumer", 2, RootScopeId, producer.InvocationId, branchId: "branch-a");
        var binding = new ActivityResultExpressionParameterBinding(ProducerNodeId, ProjectionKey);

        var resolution = _resolver.Resolve(binding, consumer, [producer, consumer]);

        Assert.NotNull(resolution);
        Assert.Equal(producer.InvocationId, resolution.ProducerInvocation.InvocationId);
        Assert.Equal(ProjectionKey, resolution.ProjectionKey);
    }

    private static RuntimeActivityResultReference Reference(bool isOptional = false) =>
        new(ProducerNodeId, ProjectionKey, RootScopeId, isOptional);

    private static ActivityExecutionState Completed(
        string invocationId,
        string nodeId,
        long sequence,
        string? scopeId,
        string? schedulingInvocationId = null,
        string? branchId = null,
        string? iterationId = null) =>
        State(invocationId, nodeId, sequence, scopeId, schedulingInvocationId, branchId, iterationId, ActivityExecutionStatus.Completed);

    private static ActivityExecutionState Running(
        string invocationId,
        string nodeId,
        long sequence,
        string? scopeId,
        string? schedulingInvocationId = null,
        string? branchId = null,
        string? iterationId = null) =>
        State(invocationId, nodeId, sequence, scopeId, schedulingInvocationId, branchId, iterationId, ActivityExecutionStatus.Running);

    private static ActivityExecutionState State(
        string invocationId,
        string nodeId,
        long sequence,
        string? scopeId,
        string? schedulingInvocationId,
        string? branchId,
        string? iterationId,
        ActivityExecutionStatus status)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence);
        var completion = status == ActivityExecutionStatus.Completed
            ? new ActivityCompletion(
                invocationId,
                $"attempt-{invocationId}",
                ValueEnvelope.Inline(
                    new ValueTypeDescriptor("System.String"),
                    JsonSerializer.SerializeToElement($"result-{invocationId}"),
                    ValueProtectionPolicy.InstanceInline),
                "done",
                timestamp,
                "contract-fingerprint")
            : null;
        var provenance = ActivitySchedulingProvenance.From(
            WorkflowExecutionId,
            parentActivityExecutionId: null,
            schedulingActivityExecutionId: schedulingInvocationId,
            branchId,
            iterationId,
            executionPathId: $"path-{invocationId}",
            executionScopeId: scopeId,
            schedulingCause: "test");

        return new ActivityExecutionState(
            new ActivityExecution(invocationId, WorkflowExecutionId, nodeId, $"authored-{nodeId}", "Test.Activity", "1.0.0"),
            status,
            SubStatus: null,
            ExecutionSequence: sequence,
            ScheduledAt: timestamp,
            StartedAt: timestamp,
            CompletedAt: completion?.CompletedAt,
            SchedulingActivityExecutionId: schedulingInvocationId,
            ParentActivityExecutionId: null,
            BranchId: branchId,
            IterationId: iterationId,
            Provenance: provenance,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>(),
            Completion: completion);
    }
}
