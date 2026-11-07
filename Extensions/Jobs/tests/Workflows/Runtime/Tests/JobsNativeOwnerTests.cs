using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeOwnerTests
{
    [Fact]
    public async Task Direct_native_execution_owns_its_actual_definition_and_invocation_without_an_optional_model_source()
    {
        var parent = NativeFixture.Parent(); var activity = NativeFixture.Activity();
        var owner = await new JobsNativeOwnerResolver([]).ResolveAsync(parent, activity, CancellationToken.None);
        Assert.Equal(parent.WorkflowExecutionId, owner.ExecutionId); Assert.Equal(activity.InvocationId, owner.InvocationId);
        Assert.Equal(parent.PinnedExecutable.DefinitionId, owner.DefinitionId); Assert.Equal(parent.PinnedExecutable.DefinitionVersionId, owner.DefinitionVersion);
        Assert.Null(owner.Origin); Assert.Null(owner.ModelAddress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_model_ownership_never_falls_back_to_the_helper_native_header(int registrations)
    {
        var parent = Parent(); var sources = Enumerable.Range(0, registrations).Select(_ => new Source(Owner(parent.ModelWorkOrigin!))).ToArray();
        var resolver = new JobsNativeOwnerResolver(sources);
        if (registrations == 0)
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => resolver.ResolveAsync(parent, NativeFixture.Activity(), CancellationToken.None).AsTask());
        else
        {
            var error = await Assert.ThrowsAsync<JobException>(() => resolver.ResolveAsync(parent, NativeFixture.Activity(), CancellationToken.None).AsTask());
            Assert.Equal("jobs.owner.ambiguous", error.Code);
        }
        Assert.All(sources, source => Assert.Equal(0, source.ResolveCalls));
    }

    [Fact]
    public async Task Model_owner_retains_the_real_root_namespace_and_logical_definition_instead_of_content_or_worker_identity()
    {
        var parent = Parent(); var expected = Owner(parent.ModelWorkOrigin!); var source = new Source(expected);
        var actual = await new JobsNativeOwnerResolver([source]).ResolveAsync(parent, NativeFixture.Activity(), CancellationToken.None);
        Assert.True(JobsNativeBoundary.Equivalent(expected, actual)); Assert.Equal("logical-process", actual.DefinitionId);
        Assert.NotEqual(parent.PinnedExecutable.DefinitionId, actual.DefinitionId); Assert.NotEqual(parent.WorkflowExecutionId, actual.ExecutionId);
        Assert.Equal(parent.ModelWorkOrigin!.Executable.DefinitionVersionId, actual.DefinitionVersion);
        Assert.True(parent.ModelWorkOrigin.IsEquivalentTo(actual.Origin)); Assert.Equal(1, source.ResolveCalls);
    }

    private static WorkflowExecutionState Parent()
    {
        var material = new WorkflowExecutableIdentity("model-material", "content-derived-definition", "material-version", "authored-display", new('c', 64));
        var origin = new RuntimeModelNativeWorkOrigin(1, new(NativeFixture.Scope.PersistencePartition), "model-root", new("elsa.bpmn.direct", 1), material,
            "model-incarnation", "service-binding", "service-invocation", "service-attempt", NativeFixture.Executable, "workflow-one",
            "source-commit:start-intent", "start-intent", "Elsa.Bpmn.NativeWorkResult.v1", "exact-root-result-input");
        return NativeFixture.Parent() with { ModelWorkOrigin = origin };
    }
    private static JobWorkflowOwner Owner(RuntimeModelNativeWorkOrigin origin) => new(1, NativeFixture.Scope, "elsa.bpmn", "logical-process",
        origin.Executable.DefinitionVersionId, origin.ExecutionId, origin.InvocationId, origin.Executable.ArtifactId, origin.Executable.ArtifactHash,
        origin, JsonSerializer.SerializeToElement(new { FormatVersion = 1, ProcessId = "logical-process", Origin = origin }));
    private sealed class Source(JobWorkflowOwner owner) : IJobWorkflowAssociationSource
    {
        internal int ResolveCalls { get; private set; }
        public bool Supports(RuntimeModelNativeWorkOrigin origin) => origin.Protocol.Key == "elsa.bpmn.direct";
        public ValueTask<JobWorkflowOwner> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default)
        { ResolveCalls++; return ValueTask.FromResult(owner); }
        public ValueTask<JobWorkflowOwnerObservation> InspectAsync(JobWorkflowOwner value, JobNativeDeliveryEvidence? evidence,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("This source test does not supply consumption or terminal proof.");
    }
}
