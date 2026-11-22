using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Characterization coverage for the shared scheduler work-handler helpers extracted in #412
/// (items 1, 2, 4). These pin the exact, byte-for-byte observable output the per-handler copies
/// produced today — wrapped-exception message text and the frozen persisted intent identifiers —
/// so the DRY extraction is proven behavior-preserving. Bite: mutating any interpolated string in
/// <see cref="SchedulerWorkHandlerHelpers"/> turns the matching assertion red.
/// </summary>
public sealed class SchedulerWorkHandlerHelpersTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 18, 0, 0, TimeSpan.Zero);

    private static readonly Func<WorkflowExecutable, string, string, WorkflowExecutableResumeTarget?>[] ResumeTargetResolvers =
    [
        WorkflowExecutableResumeTarget.FindForNode,
        SchedulerWorkHandlerHelpers.FindResumeTargetForNode
    ];

    private static readonly WorkflowExecutableIdentity Pinned =
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    // --- item 4: ValidatePinnedExecutable ---

    [Fact]
    public void ValidatePinnedExecutable_PassesWhenSnapshotMatches()
    {
        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(
            NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity), Pinned, Pinned);
    }

    [Fact]
    public void ValidatePinnedExecutable_ThrowsCommandKindKeyedMessageOnMismatch()
    {
        var loaded = Pinned with { ArtifactHash = "sha256:changed" };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(
                NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity), Pinned, loaded));

        Assert.Equal(
            "InvokeActivity scheduler work item 'work-1' loaded executable artifact 'artifact-1@1.0.0/sha256:changed (definition-1/version-1)' " +
            "but pinned executable artifact 'artifact-1@1.0.0/sha256:test (definition-1/version-1)'.",
            exception.Message);
    }

    // --- item 4: ResolveExecutableNode ---

    [Fact]
    public void ResolveExecutableNode_ReturnsNodeWhenPresent()
    {
        var executable = NewExecutable("node-1");

        var node = SchedulerWorkHandlerHelpers.ResolveExecutableNode(
            NewWorkItem(WorkflowExecutionCommandKind.ResumeBookmark), executable, "node-1", "ResumeBookmark");

        Assert.Equal("node-1", node.ExecutableNodeId);
    }

    [Fact]
    public void ResolveExecutableNode_ThrowsLabelledMessageWhenMissing()
    {
        var executable = NewExecutable("node-1");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.ResolveExecutableNode(
                NewWorkItem(WorkflowExecutionCommandKind.ResumeBookmark), executable, "node-missing", "ResumeBookmark"));

        Assert.Equal(
            "ResumeBookmark scheduler work item 'work-1' references executable node 'node-missing', " +
            "which is missing from executable artifact 'artifact-1@1.0.0/sha256:test (definition-1/version-1)'.",
            exception.Message);
    }

    // --- Resume target resolution (Core contract and compatibility forwarding helper) ---

    [Fact]
    public void FindResumeTargetForNode_PrefersDirectGlobalIdAndLeavesOwnershipCheckToCaller()
    {
        const string requestedId = "after";
        var direct = NewResumeTarget(requestedId, "node-other", "foreign-local");
        var local = NewResumeTarget("resume-opaque-node-1", "node-1", requestedId);
        var executable = NewExecutable("node-1", new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [requestedId] = direct,
            [local.ResumeTargetId] = local
        });

        foreach (var resolve in ResumeTargetResolvers)
        {
            var result = resolve(executable, "node-1", requestedId);

            Assert.Equal(direct, result);
            Assert.Equal("node-other", result!.ExecutableNodeId);
        }
    }

    [Fact]
    public void FindResumeTargetForNode_ResolvesUniqueNodeLocalKeyToOpaqueGlobalId()
    {
        const string localKey = "after";
        var otherNode = NewResumeTarget("resume-opaque-other", "node-other", localKey);
        var expected = NewResumeTarget("resume-opaque-node-1", "node-1", localKey);
        var executable = NewExecutable("node-1", new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [otherNode.ResumeTargetId] = otherNode,
            [expected.ResumeTargetId] = expected
        });

        foreach (var resolve in ResumeTargetResolvers)
        {
            var result = resolve(executable, "node-1", localKey);

            Assert.Equal(expected, result);
            Assert.Equal("resume-opaque-node-1", result!.ResumeTargetId);
        }
    }

    [Fact]
    public void FindResumeTargetForNode_ReturnsNullWhenTargetIsMissing()
    {
        var executable = NewExecutable("node-1");

        foreach (var resolve in ResumeTargetResolvers)
            Assert.Null(resolve(executable, "node-1", "missing"));
    }

    [Fact]
    public void FindResumeTargetForNode_ThrowsWhenNodeLocalKeyIsAmbiguous()
    {
        const string localKey = "after";
        var first = NewResumeTarget("resume-opaque-first", "node-1", localKey);
        var second = NewResumeTarget("resume-opaque-second", "node-1", localKey);
        var executable = NewExecutable("node-1", new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [first.ResumeTargetId] = first,
            [second.ResumeTargetId] = second
        });

        foreach (var resolve in ResumeTargetResolvers)
            Assert.Equal("Local resume target 'after' is ambiguous for executable node 'node-1'.",
                Assert.Throws<InvalidOperationException>(() => resolve(executable, "node-1", localKey)).Message);
    }

    [Fact]
    public void FindResumeTargetForNode_ValidatesArguments()
    {
        foreach (var resolve in ResumeTargetResolvers)
        {
            var nullExecutable = Assert.ThrowsAny<ArgumentException>(() => resolve(null!, "node-1", "after"));
            Assert.Equal("executable", nullExecutable.ParamName);

            var blankNode = Assert.ThrowsAny<ArgumentException>(() => resolve(NewExecutable("node-1"), " ", "after"));
            Assert.Equal("executableNodeId", blankNode.ParamName);

            var blankKey = Assert.ThrowsAny<ArgumentException>(() => resolve(NewExecutable("node-1"), "node-1", ""));
            Assert.Equal("requestedResumeTargetId", blankKey.ParamName);
        }
    }

    // --- item 1: DeserializePayload ---

    [Fact]
    public void DeserializePayload_ThrowsRequiresMessageWhenPayloadNull()
    {
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.Start, hasPayload: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.DeserializePayload(
                workItem,
                requiresPayloadMessage: "requires-message",
                resolvedToNullMessage: "null-message",
                invalidPayloadMessage: "invalid-message",
                deserialize: (_, payload) => payload.Deserialize<string>(),
                isPayloadValidationException: IsProductionShapedFilter));

        Assert.Equal("requires-message", exception.Message);
    }

    [Fact]
    public void DeserializePayload_ThrowsResolvedToNullMessageWhenDeserializeReturnsNull()
    {
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.Start);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.DeserializePayload<string?>(
                workItem,
                requiresPayloadMessage: "requires-message",
                resolvedToNullMessage: "null-message",
                invalidPayloadMessage: "invalid-message",
                deserialize: (_, _) => null,
                isPayloadValidationException: IsProductionShapedFilter));

        // The resolved-to-null throw is an InvalidOperationException, which no production catch
        // filter matches — so it must surface with the null message, never re-wrapped as invalid.
        Assert.Equal("null-message", exception.Message);
    }

    [Fact]
    public void DeserializePayload_WrapsFilteredExceptionWithInvalidMessage()
    {
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.Start);
        var inner = new ArgumentException("boom", "pinnedExecutable");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.DeserializePayload<string>(
                workItem,
                requiresPayloadMessage: "requires-message",
                resolvedToNullMessage: "null-message",
                invalidPayloadMessage: "invalid-message",
                deserialize: (_, _) => throw inner,
                isPayloadValidationException: ex => ex is ArgumentException { ParamName: "pinnedExecutable" }));

        Assert.Equal("invalid-message", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void DeserializePayload_LetsUnfilteredExceptionPropagate()
    {
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.Start);
        var inner = new ArgumentException("boom", "unrelated");

        // A caller whose filter returns false (e.g. a real bug surfacing as an unrelated
        // ArgumentException) must see the original exception, not the wrapped "invalid payload" one.
        var exception = Assert.Throws<ArgumentException>(() =>
            SchedulerWorkHandlerHelpers.DeserializePayload<string>(
                workItem,
                requiresPayloadMessage: "requires-message",
                resolvedToNullMessage: "null-message",
                invalidPayloadMessage: "invalid-message",
                deserialize: (_, _) => throw inner,
                isPayloadValidationException: ex => ex is ArgumentException { ParamName: "pinnedExecutable" }));

        Assert.Same(inner, exception);
    }

    // --- item 2: NewEnqueueSchedulerWorkIntent (frozen persisted identifiers) ---

    [Fact]
    public void NewEnqueueSchedulerWorkIntent_ProducesByteIdenticalPersistedIdentifiers()
    {
        var source = NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity, workItemId: "source-work", idempotencyKey: "wfexec-1:schedule:actexec-1");
        var follow = NewWorkItem(WorkflowExecutionCommandKind.StartActivity, workItemId: "follow-work", idempotencyKey: "wfexec-1:start:actexec-1");

        var intent = SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(source, "actexec-1", follow, Now);

        Assert.Equal("source-work:post-commit:follow-work", intent.IntentId);
        Assert.Equal("wfexec-1", intent.WorkflowExecutionId);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.Equal("Elsa.Workflows.Runtime.EnqueueSchedulerWork", intent.Kind);
        Assert.Equal(Now, intent.RecordedAt);
        Assert.Equal("actexec-1", intent.ActivityExecutionId);
        Assert.Equal("wfexec-1:schedule:actexec-1:post-commit:wfexec-1:start:actexec-1", intent.IdempotencyKey);
        Assert.Equal(source.CommandMetadata, intent.Metadata);

        // Payload is the serialized follow-up work item, round-trippable back to the same identity.
        var roundTripped = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>();
        Assert.NotNull(roundTripped);
        Assert.Equal("follow-work", roundTripped!.WorkItemId);
    }

    // --- NormalizeOutcomeNames (extracted from three byte-identical handler copies) ---

    [Fact]
    public void NormalizeOutcomeNames_ReturnsDoneForEmptyWhenDefaultToDone()
    {
        var result = SchedulerWorkHandlerHelpers.NormalizeOutcomeNames([], defaultToDone: true);

        Assert.Equal([ActivityOutcomes.Done], result);
    }

    [Fact]
    public void NormalizeOutcomeNames_ReturnsEmptyForEmptyWhenNotDefaultToDone()
    {
        var result = SchedulerWorkHandlerHelpers.NormalizeOutcomeNames([], defaultToDone: false);

        Assert.Empty(result);
    }

    [Fact]
    public void NormalizeOutcomeNames_SnapshotsAndPreservesProvidedNames()
    {
        var result = SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(["Yes", "No"], defaultToDone: true);

        Assert.Equal(["Yes", "No"], result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizeOutcomeNames_ThrowsOnBlankNames(string blank)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(["Yes", blank], defaultToDone: true));

        Assert.Equal("Activity completion outcome names cannot contain blank values.", exception.Message);
    }

    [Fact]
    public void NormalizeOutcomeNames_ThrowsOnDuplicateNames()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(["Yes", "Yes"], defaultToDone: false));

        Assert.Equal("Activity completion outcome names cannot contain duplicates.", exception.Message);
    }

    [Fact]
    public void NormalizeOutcomeNames_TreatsOrdinalCaseAsDistinct()
    {
        // Distinctness is Ordinal, so casing differences are NOT duplicates.
        var result = SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(["Done", "done"], defaultToDone: false);

        Assert.Equal(["Done", "done"], result);
    }

    // Mirrors the production catch filters, which whitelist the JSON/serialization exception family
    // and (deliberately) never include InvalidOperationException.
    private static bool IsProductionShapedFilter(Exception exception) =>
        exception is JsonException or NotSupportedException or ArgumentException;

    private static RuntimeSchedulerWorkItem NewWorkItem(
        WorkflowExecutionCommandKind commandKind,
        string workItemId = "work-1",
        string idempotencyKey = "wfexec-1:idem",
        bool hasPayload = true) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: idempotencyKey,
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 10,
            payload: hasPayload ? JsonSerializer.SerializeToElement("payload") : null,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

    private static WorkflowExecutableResumeTarget NewResumeTarget(string id, string nodeId, string localId) =>
        new(id, nodeId, "ResumeAsync", new Dictionary<string, string>(), localId);

    private static WorkflowExecutable NewExecutable(
        string nodeId,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>? resumeTargets = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new WorkflowExecutable(
            identity: Pinned,
            rootActivity: new ExecutableNode(
                executableNodeId: nodeId,
                authoredActivityId: $"authored-{nodeId}",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: resumeTargets ?? new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }
}
