using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDeliverySourceContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Source_identity_preserves_exact_component_boundaries_without_a_workflow_identity()
    {
        Assert.Equal(Address(), new("family", new PersistenceScope("private-scope"), "private-source"));
        Assert.NotEqual(Address(), new("Family", Address().Scope, Address().SourceId));
        Assert.NotEqual(new("ab", Address().Scope, "c"), new RuntimeDeliverySourceAddress("a", Address().Scope, "bc"));
        Assert.NotEqual(Address(), new("family", new PersistenceScope("other"), Address().SourceId));
        Assert.DoesNotContain("private", Address().ToString());
    }

    [Fact]
    public void Header_work_and_authority_survive_disposed_json_and_mutated_input_metadata()
    {
        var metadata = new Dictionary<string, string> { ["owner"] = "original" };
        RuntimeDeliverySourceSnapshot source;
        RuntimeDeliveryWorkClaim claim;
        using (var document = JsonDocument.Parse("{\"nested\":{\"payload\":\"private\"}}"))
        {
            source = Snapshot(document.RootElement, new("private-system", "private-root", metadata));
            claim = Claim(content: document.RootElement);
        }
        metadata["owner"] = "changed";
        Assert.Equal("private", source.Header.GetProperty("nested").GetProperty("payload").GetString());
        Assert.Equal("private", claim.Content.GetProperty("nested").GetProperty("payload").GetString());
        Assert.Equal("original", source.Authority.Metadata["owner"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)source.Authority.Metadata)["owner"] = "changed");
    }

    [Fact]
    public void Claim_requests_and_attempt_proof_references_take_read_only_snapshots()
    {
        var families = new[] { "family" }; var kinds = new[] { "dispatch" }; var proofs = new[] { "proof" };
        var request = new RuntimeDeliveryClaimRequest(Address().Scope, families, kinds, "private-owner", TimeSpan.FromMinutes(1));
        var attempt = new RuntimeDeliveryAttempt(proofs);
        families[0] = kinds[0] = proofs[0] = "changed";
        Assert.Equal("family", Assert.Single(request.Families)); Assert.Equal("dispatch", Assert.Single(request.WorkKinds));
        Assert.Equal("proof", Assert.Single(attempt.CandidateProofReferences));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)request.Families)[0] = "changed");
        Assert.Empty(new RuntimeDeliveryAttempt([]).CandidateProofReferences);
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryClaimRequest(Address().Scope, [], kinds, "owner", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryAttempt(["same", "same"]));
    }

    [Theory]
    [InlineData("source-revision")]
    [InlineData("revision")]
    [InlineData("fence")]
    [InlineData("role")]
    [InlineData("visibility")]
    public void Invalid_physical_claim_shape_refuses(string defect) => Assert.ThrowsAny<ArgumentException>(() =>
        Claim(sourceRevision: defect == "source-revision" ? 0 : 1, revision: defect == "revision" ? 0 : 1,
            fence: defect == "fence" ? 0 : 1, role: defect == "role" ? (RuntimeDeliveryWorkRole)99 : RuntimeDeliveryWorkRole.Dispatch,
            visibleAfter: defect == "visibility" ? Now : Now.AddMinutes(1)));

    [Theory]
    [InlineData("address")]
    [InlineData("revision")]
    [InlineData("header")]
    public void Context_refuses_mismatched_source_and_claim(string defect) => Assert.Throws<ArgumentException>(() =>
        new RuntimeDeliveryContext(Snapshot(), Claim(source: defect == "address" ? new("other", Address().Scope, "private-source") : Address(),
            sourceRevision: defect == "revision" ? 2 : 1, headerFingerprint: defect == "header" ? "other" : "private-header")));

    [Fact]
    public void Coherent_context_is_a_value_even_when_its_claim_is_no_longer_current()
    {
        // Current time, canonical ownership and fingerprint validation belong to the provider, not this DTO.
        var context = new RuntimeDeliveryContext(Snapshot(), Claim());
        Assert.Equal(1, context.Claim.FencingToken);
        Assert.Equal(context.Source.Address, context.Claim.Source);
        Assert.True(context.Claim.VisibleAfter < Now.AddDays(1));
        Assert.Throws<ArgumentException>(() => new RuntimeSourceCommit(context, "operation", "fingerprint", new Plan("other")));
    }

    [Fact]
    public void Boundary_preserves_actual_instances_but_does_not_admit_an_unsupported_source_store()
    {
        IRuntimeDeliverySourceStore sources = new UnsupportedSources();
        var checkpoints = Store<IRuntimeCheckpointCommitStore>(); var outbox = Store<IRuntimePostCommitOutboxStore>();
        var materials = Store<IWorkflowExecutableStore>(); var references = Store<IWorkflowExecutableSourceReferenceStore>();
        var values = Store<IDurableValueStateStore>(); var scheduler = Store<IWorkflowSchedulerWorkQueue>();
        var executions = Store<IWorkflowExecutionStateStore>(); var activities = Store<IActivityExecutionStateStore>();
        var bookmarks = Store<IBookmarkStateStore>(); var model = Store<IRuntimeModelExecutionReader>();
        var boundary = new RuntimeDeliveryBoundary(Address().Scope, sources, checkpoints, outbox, materials, references, values,
            scheduler, executions, activities, bookmarks, model);
        Assert.Same(sources, boundary.Sources); Assert.Same(checkpoints, boundary.Checkpoints); Assert.Same(outbox, boundary.Outbox);
        Assert.Same(materials, boundary.Materials); Assert.Same(references, boundary.References); Assert.Same(values, boundary.Values);
        Assert.Same(scheduler, boundary.Scheduler); Assert.Same(executions, boundary.Executions); Assert.Same(activities, boundary.Activities);
        Assert.Same(bookmarks, boundary.Bookmarks); Assert.Same(model, boundary.ModelReader);
        Assert.False(sources.CanParticipate(boundary));
    }

    [Fact]
    public async Task Completion_aware_handlers_and_dispatchers_refuse_both_bare_paths_without_preparation()
    {
        var source = NativeSource(); var handler = new CompletionHandler(); var dispatcher = new CompletionDispatcher();
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => ((IRuntimePostCommitIntentHandler)handler).HandleAsync(source.Intent).AsTask());
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => ((IRuntimePostCommitDurableSourceIntentHandler)handler).HandleAsync(source).AsTask());
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => ((IRuntimePostCommitIntentDispatcher)dispatcher).DispatchAsync(source.Intent).AsTask());
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => ((IRuntimePostCommitIntentDispatcher)dispatcher).DispatchAsync(source).AsTask());
        Assert.Equal(0, handler.Prepared); Assert.Equal(0, dispatcher.Prepared);
        await Assert.ThrowsAsync<ArgumentNullException>(() => ((IRuntimePostCommitDurableSourceIntentHandler)handler).HandleAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ((IRuntimePostCommitIntentDispatcher)dispatcher).DispatchAsync((PostCommitDeliveryContext)null!).AsTask());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => ((IRuntimePostCommitIntentHandler)handler).HandleAsync(source.Intent, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void Family_registration_snapshots_exact_sealed_plan_types_and_rejects_open_implementations()
    {
        var types = new[] { typeof(Plan) };
        var contribution = new RuntimeDeliverySourceFamilyContribution("family", 1, 1, typeof(Planner), typeof(DispatchHandler), types);
        types[0] = typeof(string);
        Assert.Equal(typeof(Plan), Assert.Single(contribution.PlanTypes));
        Assert.Throws<ArgumentException>(() => new RuntimeDeliverySourceFamilyContribution("family", 1, 1, typeof(Planner), typeof(DispatchHandler), [typeof(OpenPlan)]));
    }

    [Fact]
    public void Ordinary_formatting_redacts_payload_identity_authority_and_claim_values()
    {
        var context = new RuntimeDeliveryContext(Snapshot(), Claim()); var plan = new Plan();
        var finalization = new RuntimePostCommitSourceFinalization(1, "private-operation", "private-fingerprint", plan);
        object[] values = [context.Source.Address, context.Source, context.Claim, context, new RuntimeSourceCommit(context, "private-operation", "private-fingerprint", plan),
            new RuntimeSourceCommitResult(RuntimeSourceCommitOutcome.Committed, context.Source, "private-operation", "private-fingerprint"),
            finalization, RuntimePostCommitDispatchPreparation.FromFinalization(finalization), new RuntimeDeliveryAttempt(["private-proof"])];
        foreach (var value in values) Assert.DoesNotContain("private", $"{value}");
        Assert.Null(RuntimePostCommitDispatchPreparation.LegacyHandled.Finalization);
    }

    private static RuntimeDeliverySourceAddress Address() => new("family", new("private-scope"), "private-source");
    private static RuntimeDeliverySourceSnapshot Snapshot(JsonElement? header = null, WorkflowExecutionAuthoritySnapshot? authority = null) =>
        new(Address(), 1, 1, "private-header", header ?? JsonSerializer.SerializeToElement(new { payload = "private-payload" }), authority ?? new("private-system", "private-root"));
    private static RuntimeDeliveryWorkClaim Claim(RuntimeDeliverySourceAddress? source = null, long sourceRevision = 1, string headerFingerprint = "private-header",
        JsonElement? content = null, long revision = 1, long fence = 1, RuntimeDeliveryWorkRole role = RuntimeDeliveryWorkRole.Dispatch, DateTimeOffset? visibleAfter = null) =>
        new(source ?? Address(), sourceRevision, headerFingerprint, "private-work", "dispatch", role, content ?? JsonSerializer.SerializeToElement(new { target = "private-target" }),
            "private-content", revision, "private-owner", fence, Now, visibleAfter ?? Now.AddMinutes(1));
    private static PostCommitDeliveryContext NativeSource()
    {
        var intent = new RuntimePostCommitIntent("send", "publisher", "private-kind", Now, null, null, null);
        var item = new RuntimePostCommitOutboxItem("commit:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", Now, TimeSpan.FromMinutes(1), 1)), Address().Scope);
    }
    private sealed class Plan(string family = "family") : IRuntimeSourcePlan { public string Family { get; } = family; public int Version => 1; }
    private class OpenPlan : IRuntimeSourcePlan { public string Family => "family"; public int Version => 1; }
    private sealed class Planner : IRuntimeDeliverySourcePlanner
    { public ValueTask<RuntimeSourceCommit> PrepareAsync(RuntimeDeliveryContext source, CancellationToken cancellationToken) => throw new NotSupportedException(); }
    private sealed class DispatchHandler : IRuntimeDeliveryDispatchHandler
    {
        public ValueTask<RuntimeDeliveryAttempt> HandleAsync(RuntimeDeliveryContext child, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeSourceCommit> PrepareCompletionAsync(RuntimeDeliveryContext child, RuntimeDeliveryAttempt attempt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class UnsupportedSources : IRuntimeDeliverySourceStore
    {
        public ValueTask<RuntimeDeliverySourceSnapshot?> ReadAsync(RuntimeDeliverySourceAddress source, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RuntimeDeliveryWorkClaim>> ClaimAsync(RuntimeDeliveryClaimRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeDeliveryWorkClaim?> RenewAsync(RuntimeDeliveryWorkClaim claim, TimeSpan visibility, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RuntimeSourceCommitResult> CommitAsync(RuntimeSourceCommit commit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class CompletionHandler : IRuntimePostCommitCompletionAwareIntentHandler
    { public int Prepared { get; private set; } public ValueTask<RuntimePostCommitSourceFinalization> PrepareCompletionAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken) { Prepared++; throw new NotSupportedException(); } }
    private sealed class CompletionDispatcher : IRuntimePostCommitCompletionAwareDispatcher
    { public int Prepared { get; private set; } public ValueTask<RuntimePostCommitDispatchPreparation> PrepareDispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken) { Prepared++; throw new NotSupportedException(); } }
    private static T Store<T>() where T : class => DispatchProxy.Create<T, StoreProxy>();
    public class StoreProxy : DispatchProxy { protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(); }
}
