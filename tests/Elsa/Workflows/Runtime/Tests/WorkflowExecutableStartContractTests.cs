using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableStartContractTests
{
    private static readonly WorkflowExecutableIdentity Executable = new(
        "child-artifact",
        "child-definition",
        "child-version",
        "1",
        "child-full-hash");

    [Fact]
    public void ExistingRequestAndCommandConstructors_RemainAvailable()
    {
        Assert.NotNull(typeof(WorkflowExecutionStartDispatchRequest).GetConstructor([
            typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(IReadOnlyDictionary<string, string>), typeof(IReadOnlyDictionary<string, object>),
            typeof(IReadOnlyDictionary<string, object>), typeof(JsonElement?), typeof(string),
            typeof(WorkflowRunKind), typeof(WorkflowExecutableSourceSelection),
            typeof(WorkflowExecutableProvenanceRequirement), typeof(string), typeof(string), typeof(string),
            typeof(WorkflowExecutionPartition), typeof(WorkflowExecutionAuthoritySnapshot)
        ]));
        Assert.NotNull(typeof(WorkflowExecutionStartCommandPayload).GetConstructor([
            typeof(WorkflowExecutableIdentity), typeof(string),
            typeof(IReadOnlyDictionary<string, JsonElement>), typeof(IReadOnlyDictionary<string, JsonElement>),
            typeof(JsonElement?), typeof(string), typeof(WorkflowRunKind),
            typeof(WorkflowExecutableSourceProvenance), typeof(string), typeof(string), typeof(string),
            typeof(WorkflowExecutionPartition), typeof(WorkflowExecutionAuthoritySnapshot)
        ]));
    }

    [Fact]
    public void LegacyCommandPayload_DefaultsRetainedAuthorityToNullWithoutChangingJson()
    {
        var payload = new WorkflowExecutionStartCommandPayload(Executable, Executable.ArtifactId);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var restored = JsonSerializer.Deserialize<WorkflowExecutionStartCommandPayload>(json, JsonOptions)!;

        Assert.DoesNotContain("startAuthority", json, StringComparison.Ordinal);
        Assert.Null(restored.StartAuthority);
    }

    [Fact]
    public void StartRequest_RoundTripsAdditiveAuthorityAndDefaultsLegacyJsonToNull()
    {
        var legacy = new WorkflowExecutionStartDispatchRequest(Executable.ArtifactId, "runtime");
        var legacyJson = JsonSerializer.Serialize(legacy, JsonOptions);
        var legacyRestored = JsonSerializer.Deserialize<WorkflowExecutionStartDispatchRequest>(legacyJson, JsonOptions)!;
        var retained = new WorkflowExecutionStartDispatchRequest(
            Executable.ArtifactId,
            "runtime",
            workflowExecutionId: null,
            idempotencyKey: null,
            metadata: null,
            variables: null,
            inputs: null,
            stimulusInput: null,
            triggerNodeId: null,
            runKind: WorkflowRunKind.PublishedRun,
            sourceSelection: null,
            provenanceRequirement: WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
            parentWorkflowExecutionId: "parent-execution",
            correlationId: null,
            tenantId: "tenant-1",
            partition: null,
            authority: WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime"),
            startAuthority: WorkflowExecutableStartAuthority.FromRetainedDependency(
                "parent-artifact",
                "parent-full-hash",
                "dispatch-node"));

        var retainedRestored = JsonSerializer.Deserialize<WorkflowExecutionStartDispatchRequest>(
            JsonSerializer.Serialize(retained, JsonOptions),
            JsonOptions)!;

        Assert.DoesNotContain("startAuthority", legacyJson, StringComparison.Ordinal);
        Assert.Null(legacyRestored.StartAuthority);
        Assert.Equal(
            "parent-artifact",
            retainedRestored.StartAuthority!.RetainedDependency!.ParentArtifactId);
    }

    [Fact]
    public void RetainedDependencyAuthority_RoundTripsExactParentProvenance()
    {
        var startAuthority = WorkflowExecutableStartAuthority.FromRetainedDependency(
            "parent-artifact",
            "parent-full-hash",
            "dispatch-node");
        var payload = new WorkflowExecutionStartCommandPayload(
            Executable,
            Executable.ArtifactId,
            variables: null,
            inputs: null,
            stimulusInput: null,
            triggerNodeId: null,
            runKind: WorkflowRunKind.PublishedRun,
            pinnedSource: null,
            parentWorkflowExecutionId: "parent-execution",
            correlationId: null,
            tenantId: "tenant-1",
            partition: new WorkflowExecutionPartition("partition-1"),
            authority: WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime"),
            startAuthority: startAuthority);

        var restored = JsonSerializer.Deserialize<WorkflowExecutionStartCommandPayload>(
            JsonSerializer.Serialize(payload, JsonOptions),
            JsonOptions)!;

        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, restored.StartAuthority!.Kind);
        Assert.Equal("parent-artifact", restored.StartAuthority.RetainedDependency!.ParentArtifactId);
        Assert.Equal("parent-full-hash", restored.StartAuthority.RetainedDependency.ParentArtifactHash);
        Assert.Equal("dispatch-node", restored.StartAuthority.RetainedDependency.DispatchNodeId);
    }

    [Fact]
    public void RetainedDependencyAuthority_RejectsIncompleteOrContradictoryShapes()
    {
        Assert.Throws<ArgumentException>(() => WorkflowExecutableStartAuthority.FromRetainedDependency("parent", " ", "node"));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableStartAuthority(
            WorkflowExecutableStartAuthorityKind.RetainedDependency));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableStartAuthority(
            WorkflowExecutableStartAuthorityKind.LiveReference,
            new WorkflowExecutableRetainedDependencyAuthority("parent", "hash", "node")));
    }

    [Fact]
    public void StartModels_RejectContradictoryLiveAndRetainedAuthority()
    {
        var retained = WorkflowExecutableStartAuthority.FromRetainedDependency("parent", "hash", "node");
        var sourceSelection = new WorkflowExecutableSourceSelection(sourceReferenceId: "source-reference");
        var pinnedSource = new WorkflowExecutableSourceProvenance(
            "source-reference",
            "WorkflowDefinitionVersion",
            "version",
            "1",
            "definition",
            "version",
            "1",
            null,
            null);

        Assert.Throws<ArgumentException>(() => new WorkflowExecutionStartDispatchRequest(
            Executable.ArtifactId,
            "runtime",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            WorkflowRunKind.PublishedRun,
            sourceSelection,
            WorkflowExecutableProvenanceRequirement.RequireLiveReference,
            null,
            null,
            null,
            null,
            null,
            retained));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutionStartCommandPayload(
            Executable,
            Executable.ArtifactId,
            null,
            null,
            null,
            null,
            WorkflowRunKind.PublishedRun,
            pinnedSource,
            null,
            null,
            null,
            null,
            null,
            retained));
    }

    [Fact]
    public void StartPolicyContext_ContainsOnlyTypedImmutableStartFacts()
    {
        var context = new WorkflowExecutableStartPolicyContext(
            Executable,
            WorkflowExecutableStartAuthorityKind.RetainedDependency,
            "runtime",
            WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime"),
            WorkflowRunKind.PublishedRun,
            tenantId: "tenant-1",
            partition: new WorkflowExecutionPartition("partition-1"),
            dispatchNestingDepth: 4);

        Assert.Same(Executable, context.Executable);
        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, context.AuthorityKind);
        Assert.Equal("runtime", context.RequestedBy);
        Assert.Equal("tenant-1", context.TenantId);
        Assert.Equal("partition-1", context.Partition!.Value);
        Assert.Equal(4, context.DispatchNestingDepth);
        Assert.DoesNotContain(typeof(WorkflowExecutableStartPolicyContext).GetProperties(), property =>
            property.Name is "Inputs" or "Variables" or "StimulusInput");
    }

    [Fact]
    public void StartPolicyDecision_RequiresClassifiableSafeDenial()
    {
        var allow = WorkflowExecutableStartDecision.Allow();
        var deny = WorkflowExecutableStartDecision.Deny("artifact.start.denied", "Starts are disabled for this artifact.");

        Assert.True(allow.IsAllowed);
        Assert.False(deny.IsAllowed);
        Assert.Equal("artifact.start.denied", deny.ReasonCode);
        Assert.Equal("Starts are disabled for this artifact.", deny.Message);
        Assert.Throws<ArgumentException>(() => WorkflowExecutableStartDecision.Deny(" ", "safe"));
        Assert.Throws<ArgumentException>(() => WorkflowExecutableStartDecision.Deny("reason", " "));
    }

    [Fact]
    public void StartPolicy_IsAnExplicitSingleDecisionContract()
    {
        var method = Assert.Single(typeof(IWorkflowExecutableStartPolicy).GetMethods());

        Assert.Equal(nameof(IWorkflowExecutableStartPolicy.EvaluateAsync), method.Name);
        Assert.Equal(typeof(ValueTask<WorkflowExecutableStartDecision>), method.ReturnType);
        Assert.Equal(typeof(WorkflowExecutableStartPolicyContext), method.GetParameters()[0].ParameterType);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
}
