using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowExecutionStartDispatchTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references = new();
    private readonly RecordingAgentProvider _agentProvider = new();
    private readonly WorkflowStartDispatcher _dispatcher;

    public RuntimeWorkflowExecutionStartDispatchTests()
    {
        _dispatcher = new WorkflowStartDispatcher(
            _store,
            _references,
            _agentProvider,
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));
    }

    [Theory]
    [InlineData("source", " ")]
    [InlineData("publication", " ")]
    [InlineData("slot", " ")]
    public void SourceSelection_RejectsBlankValues(string field, string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => field switch
        {
            "source" => new WorkflowExecutableSourceSelection(sourceReferenceId: value),
            "publication" => new WorkflowExecutableSourceSelection(publicationId: value),
            _ => new WorkflowExecutableSourceSelection(slotId: value)
        });

        Assert.Equal(field switch
        {
            "source" => "sourceReferenceId",
            "publication" => "publicationId",
            _ => "slotId"
        }, exception.ParamName);
    }

    [Fact]
    public void SourceSelection_RequiresOneSelector()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableSourceSelection());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SourceSelection_RejectsMixingReferenceAndPublicationIdentity(bool includePublication, bool includeSlot)
    {
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableSourceSelection(
            sourceReferenceId: "reference-1",
            publicationId: includePublication ? "publication-1" : null,
            slotId: includeSlot ? "slot-1" : null));
    }

    [Fact]
    public async Task DispatchAsync_SendsStartCommandThroughWorkflowExecutionActor()
    {
        await ActivateAsync();

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var activation = Assert.Single(_agentProvider.ActivationRequests);
        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-1", result.WorkflowExecutionId);
        Assert.Equal("wfexec-1", activation.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorActivationReason.Start, activation.Reason);
        Assert.Equal("runtime-test", activation.RequestedBy);
        Assert.Equal("wfexec-1", envelope.WorkflowExecutionId);
        Assert.Equal("command-1", envelope.Command.CommandId);
        Assert.Equal("envelope-1", envelope.EnvelopeId);
        Assert.Equal("wfexec-1:start:artifact-1", envelope.IdempotencyKey);
        Assert.Equal(WorkflowExecutionCommandKind.Start, envelope.Command.Kind);
        Assert.Equal(_now, envelope.EnqueuedAt);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Equal(_agentProvider.Agent.Descriptor.WorkflowExecutionId, result.Agent.WorkflowExecutionId);
        Assert.Equal(_agentProvider.Agent.Descriptor.AgentId, result.Agent.AgentId);
    }

    [Fact]
    public async Task DispatchAsync_PinsExecutableIdentityInStartPayload()
    {
        await ActivateAsync();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("artifact-1", payload.RequestedArtifactId);
        Assert.Equal("artifact-1", payload.PinnedExecutable.ArtifactId);
        Assert.Equal("definition-1", payload.PinnedExecutable.DefinitionId);
        Assert.Equal("version-1", payload.PinnedExecutable.DefinitionVersionId);
        Assert.Equal("1.0.0", payload.PinnedExecutable.ArtifactVersion);
        Assert.Equal("sha256:test", payload.PinnedExecutable.ArtifactHash);
    }

    [Theory]
    [InlineData(WorkflowExecutableReferenceScope.Published, "Published")]
    [InlineData(WorkflowExecutableReferenceScope.TestRun, "TestRun")]
    public async Task DispatchAsync_CarriesDurableExecutionOrigin(
        WorkflowExecutableReferenceScope scope,
        string expectedOrigin)
    {
        await _store.SaveAsync(NewExecutable());
        await _references.SaveAsync(Reference("ref-1", "artifact-1", scope, _now.AddMinutes(30)));
        var dispatcher = NewDispatcher(new AllowWorkflowExecutableStartPolicy());

        await dispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"),
            scope);

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        Assert.Equal(expectedOrigin, envelope.Command.Metadata[RuntimeMetadataKeys.WorkflowExecutionOrigin]);
    }

    [Fact]
    public async Task DispatchAsync_PinsExplicitRunKindInStartPayload()
    {
        await ActivateAsync();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            "artifact-1",
            "weaver-background",
            runKind: WorkflowRunKind.BackgroundWeaverRun));

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(WorkflowRunKind.BackgroundWeaverRun, payload.RunKind);
    }

    [Fact]
    public async Task DispatchAsync_RejectsUnknownArtifactBeforeAgentActivation()
    {
        var exception = await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("missing-artifact", "runtime-test")).AsTask());

        Assert.Contains("missing-artifact", exception.Message);
        Assert.Equal("missing-artifact", exception.ArtifactId);
        Assert.Empty(_agentProvider.ActivationRequests);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_AuthorizesExactRetainedDependencyAndPersistsItsProvenance()
    {
        var child = NewExecutable();
        var parent = NewExecutable(
            new WorkflowExecutableIdentity("parent-artifact", "parent-definition", "parent-version", "1.0.0", "sha256:parent"),
            new WorkflowExecutableDependency("artifact-1", "sha256:test", ["node-root"]));
        await _store.SaveAsync(child);
        await _store.SaveAsync(parent);
        var authority = WorkflowExecutableStartAuthority.FromRetainedDependency(
            parent.Identity.ArtifactId,
            parent.Identity.ArtifactHash,
            "node-root");

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: child.Identity.ArtifactId,
            requestedBy: "runtime-test",
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
            tenantId: null,
            partition: null,
            authority: null,
            startAuthority: authority));

        Assert.Null(result.PinnedSource);
        var payload = Assert.Single(_agentProvider.Agent.Envelopes).Command.Payload!.Value
            .Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, payload.StartAuthority!.Kind);
        Assert.Equal(parent.Identity.ArtifactId, payload.StartAuthority.RetainedDependency!.ParentArtifactId);
        Assert.Equal(parent.Identity.ArtifactHash, payload.StartAuthority.RetainedDependency.ParentArtifactHash);
        Assert.Equal("node-root", payload.StartAuthority.RetainedDependency.DispatchNodeId);
    }

    [Theory]
    [InlineData("missing-parent", "sha256:parent", "node-root", "sha256:test")]
    [InlineData("parent-artifact", "sha256:other", "node-root", "sha256:test")]
    [InlineData("parent-artifact", "sha256:parent", "node-other", "sha256:test")]
    [InlineData("parent-artifact", "sha256:parent", "node-root", "sha256:other-child")]
    public async Task DispatchAsync_RejectsRetainedDependencyMismatchBeforePolicyOrActor(
        string parentArtifactId,
        string parentArtifactHash,
        string dispatchNodeId,
        string dependencyChildHash)
    {
        var policy = new RecordingStartPolicy(WorkflowExecutableStartDecision.Allow());
        var dispatcher = NewDispatcher(policy);
        var child = NewExecutable();
        var parent = NewExecutable(
            new WorkflowExecutableIdentity("parent-artifact", "parent-definition", "parent-version", "1.0.0", "sha256:parent"),
            new WorkflowExecutableDependency("artifact-1", dependencyChildHash, ["node-root"]));
        await _store.SaveAsync(child);
        await _store.SaveAsync(parent);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => dispatcher.DispatchAsync(
            RetainedRequest(child.Identity.ArtifactId, parentArtifactId, parentArtifactHash, dispatchNodeId)).AsTask());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Authority, exception.Category);
        Assert.StartsWith("retained-dependency-", exception.ReasonCode, StringComparison.Ordinal);
        Assert.Empty(policy.Contexts);
        Assert.Empty(_agentProvider.ActivationRequests);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_EvaluatesPolicyAfterRetainedAuthorityAndBeforeActorLookup()
    {
        var policy = new RecordingStartPolicy(WorkflowExecutableStartDecision.Deny("artifact-retired", "This artifact is retired."));
        var dispatcher = NewDispatcher(policy);
        var child = NewExecutable();
        var parent = NewExecutable(
            new WorkflowExecutableIdentity("parent-artifact", "parent-definition", "parent-version", "1.0.0", "sha256:parent"),
            new WorkflowExecutableDependency("artifact-1", "sha256:test", ["node-root"]));
        await _store.SaveAsync(child);
        await _store.SaveAsync(parent);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => dispatcher.DispatchAsync(
            RetainedRequest(child.Identity.ArtifactId, parent.Identity.ArtifactId, parent.Identity.ArtifactHash, "node-root")).AsTask());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Policy, exception.Category);
        Assert.Equal("artifact-retired", exception.ReasonCode);
        Assert.Equal("This artifact is retired.", exception.Message);
        var context = Assert.Single(policy.Contexts);
        Assert.Equal(child.Identity, context.Executable);
        Assert.Equal(WorkflowExecutableStartAuthorityKind.RetainedDependency, context.AuthorityKind);
        Assert.Empty(_agentProvider.ActivationRequests);
        Assert.Empty(_agentProvider.Agent.Envelopes);
        Assert.Same(child, await _store.FindAsync(child.Identity.ArtifactId));
        Assert.Same(parent, await _store.FindAsync(parent.Identity.ArtifactId));
    }

    [Fact]
    public async Task DispatchAsync_AllowedPolicyRunsBeforeActorLookupAndPermitsStart()
    {
        var policy = new RecordingStartPolicy(WorkflowExecutableStartDecision.Allow());
        var dispatcher = NewDispatcher(policy);
        await ActivateAsync();

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        Assert.Single(policy.Contexts);
        Assert.Single(_agentProvider.ActivationRequests);
        Assert.Single(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesReferenceLessArtifactWithNullLegacyProvenance()
    {
        await _store.SaveAsync(NewExecutable());

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Null(result.PinnedSource);
        Assert.Null(payload.PinnedSource);
        Assert.Equal("1.0.0", envelope.Command.Metadata["runtime.artifactVersion"]);
    }

    [Fact]
    public async Task DispatchAsync_RejectsExplicitSelectionForReferenceLessArtifact()
    {
        await _store.SaveAsync(NewExecutable());

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "runtime-test",
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "missing-reference"))).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.SelectionNotFound, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesArtifactWithLivePublishedReference()
    {
        await ActivateAsync(references: Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Single(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_PinsProvenanceFromLivePublishedReferenceInsteadOfStoredDraftIdentity()
    {
        var draftIdentity = new WorkflowExecutableIdentity(
            "artifact-1",
            "definition-1",
            "draft:snapshot-1",
            "draft",
            "sha256:test");
        await ActivateAsync(
            NewExecutable(draftIdentity),
            Reference("ref-published", "artifact-1", WorkflowExecutableReferenceScope.Published));

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("draft:snapshot-1", payload.PinnedExecutable.DefinitionVersionId);
        Assert.Equal("draft", payload.PinnedExecutable.ArtifactVersion);
        Assert.Equal(payload.PinnedExecutable, result.PinnedExecutable);
        Assert.Equal("version-1", payload.PinnedSource!.DefinitionVersionId);
        Assert.Equal("1.0.0", payload.PinnedSource.ArtifactVersion);
        Assert.Equal("1.0.0", envelope.Command.Metadata["runtime.artifactVersion"]);
    }

    [Fact]
    public async Task DispatchAsync_RequiresExplicitSelectionWhenPublishedArtifactHasMultipleLiveReferences()
    {
        await ActivateAsync(
            references:
            [
                Reference("ref-v1", "artifact-1", WorkflowExecutableReferenceScope.Published),
                Reference(
                    "ref-v2", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    definitionVersionId: "version-2", artifactVersion: "2.0.0")
            ]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test")).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Ambiguous, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_ValidatesExplicitSourceReferenceAndPinsItsServerOwnedProvenance()
    {
        await ActivateAsync(
            references:
            [
                Reference("ref-v1", "artifact-1", WorkflowExecutableReferenceScope.Published),
                Reference(
                    "ref-v2", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    definitionVersionId: "version-2", artifactVersion: "2.0.0")
            ]);

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            "artifact-1",
            "runtime-test",
            sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-v2")));

        Assert.Equal("version-2", result.PinnedSource!.DefinitionVersionId);
        Assert.Equal("2.0.0", result.PinnedSource.ArtifactVersion);
        Assert.Equal("sha256:test", result.PinnedExecutable.ArtifactHash);
        Assert.Equal("ref-v2", result.PinnedSource!.SourceReferenceId);
        Assert.Equal("ref-v2", Assert.Single(_agentProvider.Agent.Envelopes).Command.Payload!.Value
            .Deserialize<WorkflowExecutionStartCommandPayload>()!.PinnedSource!.SourceReferenceId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DispatchAsync_SelectsByPublicationOrSlotAlone(bool selectPublication)
    {
        await ActivateAsync(
            references:
            [
                Reference(
                    "ref-blue", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    publicationId: "publication-blue", slotId: "slot-blue"),
                Reference(
                    "ref-green", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    definitionVersionId: "version-green", artifactVersion: "3.0.0",
                    publicationId: "publication-green", slotId: "slot-green")
            ]);

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            "artifact-1",
            "http-stimulus",
            sourceSelection: new WorkflowExecutableSourceSelection(
                publicationId: selectPublication ? "publication-green" : null,
                slotId: selectPublication ? null : "slot-green")));

        Assert.Equal("ref-green", result.PinnedSource!.SourceReferenceId);
        Assert.Equal("version-green", result.PinnedSource!.DefinitionVersionId);
    }

    [Fact]
    public async Task DispatchAsync_SelectsPublicationOwnedByStimulusBinding()
    {
        await ActivateAsync(
            references:
            [
                Reference(
                    "ref-blue", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    definitionVersionId: "version-blue", artifactVersion: "2.0.0",
                    publicationId: "publication-blue", slotId: "slot-blue"),
                Reference(
                    "ref-green", "artifact-1", WorkflowExecutableReferenceScope.Published,
                    definitionVersionId: "version-green", artifactVersion: "3.0.0",
                    publicationId: "publication-green", slotId: "slot-green")
            ]);

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            "artifact-1",
            "http-stimulus",
            sourceSelection: new WorkflowExecutableSourceSelection(
                publicationId: "publication-green",
                slotId: "slot-green")));

        Assert.Equal("version-green", result.PinnedSource!.DefinitionVersionId);
        Assert.Equal("3.0.0", result.PinnedSource.ArtifactVersion);
    }

    [Fact]
    public async Task DispatchAsync_RejectsSourceReferenceFromAnotherArtifact()
    {
        await ActivateAsync(
            references:
            [
                Reference("ref-artifact-1", "artifact-1", WorkflowExecutableReferenceScope.Published),
                Reference("ref-artifact-2", "artifact-2", WorkflowExecutableReferenceScope.Published)
            ]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "runtime-test",
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-artifact-2"))).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.SelectionNotFound, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_RejectsSelectedReferenceFromTheWrongScope()
    {
        await ActivateAsync(
            references:
            [Reference(
                "ref-test-run", "artifact-1", WorkflowExecutableReferenceScope.TestRun,
                expiresAt: _now.AddMinutes(30))]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "runtime-test",
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-test-run"))).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.SelectionNotFound, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_RejectsAnExplicitlySelectedExpiredReferenceInsteadOfFallingBack()
    {
        await ActivateAsync(
            references:
            [
                Reference(
                    "ref-expired", "artifact-1", WorkflowExecutableReferenceScope.TestRun,
                    expiresAt: _now.AddMinutes(-1)),
                Reference(
                    "ref-live", "artifact-1", WorkflowExecutableReferenceScope.TestRun,
                    expiresAt: _now.AddMinutes(30))
            ]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    "artifact-1",
                    "test-run",
                    sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-expired")),
                WorkflowExecutableReferenceScope.TestRun).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Expired, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_PinsTheExactSelectedTestRunReference()
    {
        await ActivateAsync(
            references:
            [
                Reference(
                    "ref-test-one", "artifact-1", WorkflowExecutableReferenceScope.TestRun,
                    expiresAt: _now.AddMinutes(30), definitionVersionId: "draft:snapshot-1", artifactVersion: "draft"),
                Reference(
                    "ref-test-two", "artifact-1", WorkflowExecutableReferenceScope.TestRun,
                    expiresAt: _now.AddMinutes(30), definitionVersionId: "version-2", artifactVersion: "2.0.0")
            ]);

        var result = await _dispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "test-run",
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-test-one")),
            WorkflowExecutableReferenceScope.TestRun);

        Assert.Equal("draft:snapshot-1", result.PinnedSource!.DefinitionVersionId);
        Assert.Equal("draft", result.PinnedSource.ArtifactVersion);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotFallBackWhenExplicitReferenceIsRetired()
    {
        await ActivateAsync(
            references:
            [
                Reference("ref-live", "artifact-1", WorkflowExecutableReferenceScope.Published),
                Reference("ref-retired", "artifact-1", WorkflowExecutableReferenceScope.Published)
            ]);
        await _references.RetireAsync("ref-retired", _now.AddMinutes(-1));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "runtime-test",
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "ref-retired"))).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_PinnedIdentityRemainsImmutableAfterReferenceRetirement()
    {
        await ActivateAsync(
            references:
            [Reference(
                "ref-v2", "artifact-1", WorkflowExecutableReferenceScope.Published,
                definitionVersionId: "version-2", artifactVersion: "2.0.0")]);

        var result = await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));
        var payloadBeforeRetirement = Assert.Single(_agentProvider.Agent.Envelopes).Command.Payload!.Value
            .Deserialize<WorkflowExecutionStartCommandPayload>()!;

        await _references.RetireAsync("ref-v2", _now.AddMinutes(1), "superseded");

        Assert.Equal("version-2", result.PinnedSource!.DefinitionVersionId);
        Assert.Equal("2.0.0", result.PinnedSource.ArtifactVersion);
        Assert.Equal(result.PinnedExecutable, payloadBeforeRetirement.PinnedExecutable);
        Assert.Equal("ref-v2", payloadBeforeRetirement.PinnedSource!.SourceReferenceId);
        Assert.Equal("ref-v2", result.PinnedSource!.SourceReferenceId);
    }

    [Fact]
    public async Task DispatchAsync_RejectsPublishedDispatchOfArtifactWithOnlyTestRunReference()
    {
        await ActivateAsync(
            references:
            [Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(30))]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test")).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_RejectsWithExpiryReasonWhenOnlyMatchingReferenceExpired()
    {
        await ActivateAsync(
            references:
            [Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1))]);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "test-run"), WorkflowExecutableReferenceScope.TestRun).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Expired, exception.Reason);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_CarriesSeededVariablesAndInputsIntoStartPayload()
    {
        await ActivateAsync();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: "artifact-1",
            requestedBy: "test",
            variables: new Dictionary<string, object?> { ["greeting"] = "Hello" },
            inputs: new Dictionary<string, object?> { ["name"] = "World" }));

        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("Hello", payload.Variables["greeting"].GetString());
        Assert.Equal("World", payload.Inputs["name"].GetString());
    }

    [Fact]
    public async Task DispatchAsync_UsesProvidedWorkflowExecutionIdAndIdempotencyKey()
    {
        await ActivateAsync();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: "artifact-1",
            requestedBy: "test",
            workflowExecutionId: "wfexec-provided",
            idempotencyKey: "caller-key",
            metadata: new Dictionary<string, string> { ["caller"] = "unit-test" }));

        var activation = Assert.Single(_agentProvider.ActivationRequests);
        var envelope = Assert.Single(_agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-provided", activation.WorkflowExecutionId);
        Assert.Equal("test", activation.RequestedBy);
        Assert.Equal("caller-key", envelope.IdempotencyKey);
        Assert.Equal("unit-test", envelope.Command.Metadata["caller"]);
        Assert.Equal("artifact-1", envelope.Command.Metadata["runtime.artifactId"]);
    }

    [Fact]
    public async Task DispatchAsync_ForwardsDispatchOptionsToAgentEnqueue()
    {
        // Spec 089 E-D4 (FR-019): the dispatcher forwards the caller's dispatch options verbatim into the agent
        // mailbox so an in-process inline drain builds activity execution contexts from the ambient request scope.
        await ActivateAsync();
        var options = new WorkflowExecutionCommandDispatchOptions();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"), dispatchOptions: options);

        Assert.Same(options, Assert.Single(_agentProvider.Agent.DispatchOptions));
    }

    [Fact]
    public async Task DispatchAsync_WithoutDispatchOptions_EnqueuesDefaultOptions()
    {
        // Absent options ⇒ the dispatcher enqueues WorkflowExecutionCommandDispatchOptions.Default, pinning the
        // pre-089 single-arg behavior (no ambient services attached).
        await ActivateAsync();

        await _dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var options = Assert.Single(_agentProvider.Agent.DispatchOptions);
        Assert.Same(WorkflowExecutionCommandDispatchOptions.Default, options);
        Assert.Null(options!.AmbientServices);
    }

    private async Task ActivateAsync(
        WorkflowExecutable? executable = null,
        params WorkflowExecutableSourceReference[] references)
    {
        await _store.SaveAsync(executable ?? NewExecutable());
        var sourceReferences = references.Length == 0
            ? [Reference("ref-default", "artifact-1", WorkflowExecutableReferenceScope.Published)]
            : references;
        foreach (var reference in sourceReferences)
            await _references.SaveAsync(reference);
    }

    private WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? expiresAt = null,
        string definitionVersionId = "version-1",
        string artifactVersion = "1.0.0",
        string? publicationId = null,
        string? slotId = null) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: definitionVersionId,
            SourceVersion: artifactVersion,
            DefinitionId: "definition-1",
            DefinitionVersionId: definitionVersionId,
            ArtifactVersion: artifactVersion,
            CreatedAt: _now,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? _now : null,
            Scope: scope,
            ExpiresAt: expiresAt,
            PublicationId: publicationId,
            SlotId: slotId);

    private static WorkflowExecutable NewExecutable(
        WorkflowExecutableIdentity? identity = null,
        params WorkflowExecutableDependency[] dependencies) =>
        new(
            identity: identity ?? new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: NewNode("node-root"),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private WorkflowStartDispatcher NewDispatcher(IWorkflowExecutableStartPolicy policy) =>
        new(
            _store,
            _references,
            _agentProvider,
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now),
            partitionAccessor: null,
            startPolicy: policy);

    private static WorkflowExecutionStartDispatchRequest RetainedRequest(
        string childArtifactId,
        string parentArtifactId,
        string parentArtifactHash,
        string dispatchNodeId) =>
        new(
            artifactId: childArtifactId,
            requestedBy: "runtime-test",
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
            tenantId: null,
            partition: null,
            authority: null,
            startAuthority: WorkflowExecutableStartAuthority.FromRetainedDependency(parentArtifactId, parentArtifactHash, dispatchNodeId));

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { type = "test" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider
    {
        public RecordingAgent Agent { get; } = new("wfexec-unassigned");
        public List<WorkflowExecutionActorActivationRequest> ActivationRequests { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivationRequests.Add(request);
            Agent.AssignWorkflowExecutionId(request.WorkflowExecutionId);
            return ValueTask.FromResult<IWorkflowExecutionActor>(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionActor
    {
        private string _workflowExecutionId = workflowExecutionId;

        public WorkflowExecutionActorDescriptor Descriptor { get; private set; } = NewDescriptor(workflowExecutionId);
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public List<WorkflowExecutionCommandDispatchOptions?> DispatchOptions { get; } = [];

        public void AssignWorkflowExecutionId(string workflowExecutionId)
        {
            _workflowExecutionId = workflowExecutionId;
            Descriptor = NewDescriptor(workflowExecutionId);
        }

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            Record(envelope, null);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default) =>
            Record(envelope, options);

        private ValueTask<WorkflowExecutionCommandDispatchResult> Record(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions? options)
        {
            Envelopes.Add(envelope);
            DispatchOptions.Add(options);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: _workflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UtcNow));
        }

        private static WorkflowExecutionActorDescriptor NewDescriptor(string workflowExecutionId) =>
            new(
                workflowExecutionId: workflowExecutionId,
                agentId: $"recording:{workflowExecutionId}",
                providerName: "Recording",
                status: WorkflowExecutionActorStatus.Active,
                capabilities: WorkflowExecutionActorCapabilities.None,
                activatedAt: DateTimeOffset.UtcNow);
    }

    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _workflowExecutionIndex;
        private int _commandIndex;
        private int _envelopeIndex;

        public string NewWorkflowExecutionId() => $"wfexec-{++_workflowExecutionIndex}";

        public string NewWorkflowExecutionCommandId() => $"command-{++_commandIndex}";

        public string NewWorkflowExecutionCommandEnvelopeId() => $"envelope-{++_envelopeIndex}";

        public string NewActivityExecutionId() => throw new NotSupportedException("Start dispatch does not schedule activities.");
    }

    private sealed class RecordingStartPolicy(WorkflowExecutableStartDecision decision) : IWorkflowExecutableStartPolicy
    {
        public List<WorkflowExecutableStartPolicyContext> Contexts { get; } = [];

        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(
            WorkflowExecutableStartPolicyContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
