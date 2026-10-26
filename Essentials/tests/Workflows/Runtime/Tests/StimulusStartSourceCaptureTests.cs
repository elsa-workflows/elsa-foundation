using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual ordinary reference selection only; no cohort acceptance, saved start or retention proof.</summary>
public sealed class StimulusStartSourceCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "producer-definition", "producer-version", "1.0.0", "hash");

    [Fact]
    public async Task Capture_uses_selected_publication_attribution_and_never_substitutes_another_after_retirement()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore(); var clock = new FakeTimeProvider(Now);
        var selected = Reference("selected", "activation-a"); var other = Reference("other", "activation-b");
        await store.SaveAsync(selected); await store.SaveAsync(other);
        var resolver = new WorkflowExecutableStartSourceResolver(store, clock);
        var captured = await resolver.CaptureAsync(Binding(), Pin);
        Assert.True(captured.SourceProvenanceCaptured);
        Assert.Equal(WorkflowExecutableSourceProvenance.From(selected), captured.SourceProvenance);
        Assert.Equal("selected-version", captured.SourceProvenance!.DefinitionVersionId);
        Assert.Equal("producer-version", captured.Executable.DefinitionVersionId);
        clock.Advance(TimeSpan.FromSeconds(1)); await store.SaveAsync(selected.Retire(clock.GetUtcNow()));
        var refused = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() => resolver.CaptureAsync(Binding(), Pin).AsTask());
        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, refused.Reason);
        Assert.Equal("selected", captured.SourceProvenance.SourceReferenceId);
        Assert.Equal("activation-a", captured.SourceProvenance.PublicationId);
    }

    [Fact]
    public async Task Capture_explicitly_records_reference_less_legacy_only_when_no_reference_or_selector_exists()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        var resolver = new WorkflowExecutableStartSourceResolver(store, new FakeTimeProvider(Now));
        var captured = await resolver.CaptureAsync(Binding() with { ActivationId = null, SlotId = null }, Pin);
        Assert.True(captured.SourceProvenanceCaptured); Assert.Null(captured.SourceProvenance);
        var refused = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() => resolver.CaptureAsync(Binding(), Pin).AsTask());
        Assert.Equal(WorkflowExecutableReferenceRejectionReason.SelectionNotFound, refused.Reason);
    }

    [Fact]
    public async Task Legacy_unselected_binding_captures_the_single_actual_live_reference_instead_of_null()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore(); var reference = Reference("selected", "activation-a");
        await store.SaveAsync(reference);
        var captured = await new WorkflowExecutableStartSourceResolver(store, new FakeTimeProvider(Now))
            .CaptureAsync(Binding() with { ActivationId = null, SlotId = null }, Pin);
        Assert.Equal(WorkflowExecutableSourceProvenance.From(reference), captured.SourceProvenance);
    }

    [Theory]
    [InlineData("missing", WorkflowExecutableReferenceRejectionReason.SelectionNotFound)]
    [InlineData("retired", WorkflowExecutableReferenceRejectionReason.NoLiveReference)]
    [InlineData("wrong-scope", WorkflowExecutableReferenceRejectionReason.SelectionNotFound)]
    [InlineData("expired", WorkflowExecutableReferenceRejectionReason.Expired)]
    [InlineData("ambiguous", WorkflowExecutableReferenceRejectionReason.Ambiguous)]
    public async Task Capture_preserves_ordinary_eligibility_refusals(string defect, WorkflowExecutableReferenceRejectionReason expected)
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore(); var reference = Reference("selected", "activation-a");
        if (defect != "missing") await store.SaveAsync(defect switch
        {
            "retired" => reference.Retire(Now),
            "wrong-scope" => reference with { Scope = WorkflowExecutableReferenceScope.TestRun },
            "expired" => reference with { ExpiresAt = Now },
            _ => reference
        });
        if (defect == "ambiguous") await store.SaveAsync(reference with { SourceReferenceId = "second" });
        var refused = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            new WorkflowExecutableStartSourceResolver(store, new FakeTimeProvider(Now)).CaptureAsync(Binding(), Pin).AsTask());
        Assert.Equal(expected, refused.Reason);
    }

    [Fact]
    public async Task Capture_traverses_all_reference_pages_before_deciding_uniqueness()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await store.SaveAsync(Reference($"reference-{index:D5}", index == 0 || index == RuntimeStorePageRequest.MaximumLimit
                ? "activation-a" : "other-" + index));
        var refused = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            new WorkflowExecutableStartSourceResolver(store, new FakeTimeProvider(Now)).CaptureAsync(Binding(), Pin).AsTask());
        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Ambiguous, refused.Reason);
    }

    private static WorkflowTriggerBinding Binding() => new("binding", Pin.ArtifactId, Pin.DefinitionId, Pin.ArtifactVersion,
        Pin.ArtifactHash, "start", "event", "named", null, new Dictionary<string, string>(), Now, "activation-a", "slot");

    private static WorkflowExecutableSourceReference Reference(string id, string activation) => new(id, Pin.ArtifactId,
        "WorkflowDefinitionVersion", "selected-source", "2", "selected-definition", "selected-version", "selected-label",
        Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: "slot");
}
