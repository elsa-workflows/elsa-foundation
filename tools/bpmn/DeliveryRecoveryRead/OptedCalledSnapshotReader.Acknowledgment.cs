using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static partial class OptedCalledSnapshotReader
{
    internal static async Task<object> ReadAcknowledgmentAsync(string snapshot, string scopeName, string apiPath, string markerPath, string phase)
    {
        Require(phase is "held" or "recovered" or "finished", "Unknown acknowledgment observation phase.");
        using var apiDocument = JsonDocument.Parse(await File.ReadAllTextAsync(apiPath));
        using var markerDocument = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath));
        var api = apiDocument.RootElement; var marker = markerDocument.RootElement;
        Require(api.GetProperty("passed").GetBoolean() && api.GetProperty("phase").GetString() is "Observe" or "FinishWarning" && api.GetProperty("cases").GetArrayLength() == 1, "Actual public acknowledgment-stage observation required.");
        Require(marker.GetProperty("phase").GetString() == "called-control-before-acknowledgment" && marker.GetProperty("providerEvent").GetString() == "ActualIntentDispatcherReturnedBeforeDeliveryResult" && marker.GetProperty("scope").GetString() == scopeName,
            "The actual owned pre-acknowledgment dispatcher cut is required.");
        Require(marker.GetProperty("capturedAtUtc").GetDateTimeOffset() < marker.GetProperty("visibleAfter").GetDateTimeOffset(), "The observed dispatcher cut did not occur under its live claim.");
        var control = marker.GetProperty("transport").Deserialize<RuntimeChildControlTransport>(Json) ?? throw new InvalidDataException("Actual cut lost its source transport.");
        var terminal = control.Batch.IsTerminal; var ordinal = terminal ? 1L : 0L;
        Require(control.Batch.Ordinal == ordinal && control.Batch.ConsumerKey == ConsumerKey && control.Batch.ConsumerVersion == 1 && control.Batch.AuthoredFailureCode is null &&
            marker.GetProperty("intentKind").GetString() == (terminal ? WorkflowDispatchLifecycle.ResumeParentIntentKind : NotifyKind), "The actual cut does not select notification zero or success terminal one.");
        if (terminal) Require(control.Batch.Notifications.Count == 0, "Actual terminal one fabricated a notice."); else AssertWarning(control.Batch);
        var expectedStage = phase == "finished" ? "Finished" : terminal ? "TerminalAccepted" : "NoticeAccepted";
        Require(api.GetProperty("expectedStage").GetString() == expectedStage, "Actual public stage does not match the cut.");
        var item = api.GetProperty("cases")[0];
        var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!; var childId = item.GetProperty("childWorkflowExecutionId").GetString()!;
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var query = connection.CreateCommand()) { query.CommandText = "PRAGMA query_only=ON"; await query.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName));
        var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging()); var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
        var dispatches = new EfWorkflowDispatchStore(context, access); var incidents = new EfIncidentStateStore(context, access); var bookmarks = new EfBookmarkStateStore(context, access);
        var material = new EfWorkflowExecutableStore(context, access); var outbox = new EfRuntimePostCommitOutboxStore(context, access); var poison = new EfWorkflowSchedulerPoisonStore(context, access);
        var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName && row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)), "An enumerated physical source differs from its checked scoped identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("Checked actual source disappeared."));
        }
        var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("Actual caller missing."); var child = await workflows.FindAsync(childId) ?? throw new InvalidDataException("Actual child missing.");
        MatchPin(parent, item.GetProperty("parent").GetProperty("instance")); MatchPin(child, item.GetProperty("child").GetProperty("instance"));
        var parentStates = await activities.ListAllAsync(parentId); var childStates = await activities.ListAllAsync(childId);
        var root = Exact(parentStates, item.GetProperty("parent"), "parent-bpmn"); var childRoot = Exact(childStates, item.GetProperty("child"), "child-bpmn"); var call = Exact(parentStates, item.GetProperty("parent"), "node-call");
        var completedPair = terminal || phase == "finished";
        var dispatch = (await dispatches.ListAsync(parentId)).Single();
        Require(parent.ParentWorkflowExecutionId is null && child.ParentWorkflowExecutionId == parentId && parent.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced" && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced" &&
            parent.Status == (phase == "finished" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) && child.Status == (completedPair ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) &&
            root.ParentActivityExecutionId is null && childRoot.ParentActivityExecutionId is null &&
            root.Status == (phase == "finished" ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Running) && childRoot.Status == (completedPair ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Running) &&
            call.Status == (completedPair ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended) && call.IterationId is null && call.ParentActivityExecutionId == root.InvocationId && call.Provenance.ParentActivityExecutionId == root.InvocationId &&
            dispatch.Mode == WorkflowDispatchMode.WaitForCompletion && dispatch.ParentWorkflowExecutionId == parentId && dispatch.ParentActivityExecutionId == call.InvocationId && dispatch.ChildWorkflowExecutionId == childId && dispatch.Status == (completedPair ? WorkflowDispatchStatus.Completed : WorkflowDispatchStatus.Started) &&
            WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dispatch.ChildExecutable, child.PinnedExecutable) && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlDispatchId) == dispatch.DispatchId,
            "Actual committed caller/child call boundary or status differs.");
        if (phase == "finished") Require(root.PrivateState is null, "The finished actual caller retained semantic structural state.");
        Declares(dispatch.Metadata); Declares(call.Provenance.Metadata);
        var executable = await material.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Pinned actual caller missing.");
        Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable), "Actual pinned caller material changed."); Declares(executable.NodesById[root.Execution.ExecutableNodeId].Metadata);
        Require(control.ParentWorkflowExecutionId == parentId && control.Batch.ChildWorkflowExecutionId == childId && control.Batch.RootActivityExecutionId == childRoot.InvocationId && control.ConsumerActivityExecutionId == root.InvocationId &&
            control.CallingActivityExecutionId == call.InvocationId && control.DispatchId == dispatch.DispatchId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(control.ParentExecutable, parent.PinnedExecutable), "The actual cut addresses a different admitted boundary.");
        var source = sources.Single(candidate => candidate.OutboxItemId == control.SourceOutboxItemId);
        Require(source.OutboxItemId == marker.GetProperty("sourceOutboxItemId").GetString() && control.SourceCommitId == marker.GetProperty("sourceCommitId").GetString() && source.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(control.SourceCommitId, control.SourceIntentId) && source.Intent.IntentId == control.SourceIntentId &&
            RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent) == marker.GetProperty("intentFingerprint").GetString() && source.Intent.Kind == (terminal ? WorkflowDispatchLifecycle.ResumeParentIntentKind : NotifyKind) && source.Intent.WorkflowExecutionId == (terminal ? parentId : childId) && source.Intent.WorkflowExecutionId == marker.GetProperty("workflowExecutionId").GetString(), "The cut source no longer has its exact immutable identity/content/address.");
        var payload = source.Intent.Payload!.Value;
        var transported = (terminal ? payload.GetProperty("result").GetProperty("control") : payload).Deserialize<RuntimeChildControlTransport>(Json)!;
        Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(transported, Json), JsonSerializer.SerializeToElement(control, Json)), "The cut's transport differs from its immutable actual source.");
        if (phase == "held") Require(source.Status == RuntimePostCommitOutboxStatus.Delivering && source.DeliveringOwnerId == marker.GetProperty("ownerId").GetString() && source.DeliveryFencingToken == marker.GetProperty("fencingToken").GetInt64() && source.DeliveryVisibleAfter == marker.GetProperty("visibleAfter").GetDateTimeOffset() && source.DeliveredAt is null, "The actual source has already acknowledged or lost its observed claim.");
        else Require(source.Status == RuntimePostCommitOutboxStatus.Delivered && source.DeliveryFencingToken > marker.GetProperty("fencingToken").GetInt64() && source.DeliveredAt >= marker.GetProperty("visibleAfter").GetDateTimeOffset() && source.DeliveringOwnerId is null, "A fresh standard owner has not durably acknowledged the original source after its natural deadline.");
        var inbox = call.ChildControlInbox ?? throw new InvalidDataException("Actual caller has no committed accepted inbox.");
        Require(inbox.NextOrdinal == (completedPair ? 2 : ordinal + 1) && inbox.IsTerminalConsumed == completedPair && inbox.Pending.Count == 0 && inbox.HasAccepted(control) && inbox.Target.MatchesAddress(control), "Actual caller acceptance is not committed before source acknowledgment.");
        var finalControl = control;
        if (phase == "finished" && !terminal)
        {
            finalControl = call.ChildControl ?? throw new InvalidDataException("The finished notice-cut caller lost its later real terminal receipt.");
            Require(finalControl.Batch.IsTerminal && finalControl.Batch.Ordinal == 1 && finalControl.Batch.ConsumerKey == ConsumerKey &&
                finalControl.Batch.ConsumerVersion == 1 && finalControl.Batch.Notifications.Count == 0 &&
                finalControl.Batch.AuthoredFailureCode is null && inbox.HasAccepted(finalControl) && inbox.Target.MatchesAddress(finalControl) &&
                finalControl.DispatchId == control.DispatchId && finalControl.Batch.ChildWorkflowExecutionId == childId &&
                finalControl.Batch.RootActivityExecutionId == childRoot.InvocationId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(finalControl.ParentExecutable, parent.PinnedExecutable),
                "The finished notice-cut caller's real terminal acceptance differs.");
            var terminalSource = sources.Single(candidate => candidate.OutboxItemId == finalControl.SourceOutboxItemId);
            Require(terminalSource.Status == RuntimePostCommitOutboxStatus.Delivered && terminalSource.Intent.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind &&
                terminalSource.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(finalControl.SourceCommitId, finalControl.SourceIntentId) &&
                terminalSource.Intent.WorkflowExecutionId == parentId && terminalSource.Intent.IntentId == finalControl.SourceIntentId &&
                terminalSource.Intent.Payload is { } terminalPayload && JsonElement.DeepEquals(
                    JsonSerializer.SerializeToElement(terminalPayload.GetProperty("result").GetProperty("control").Deserialize<RuntimeChildControlTransport>(Json), Json),
                    JsonSerializer.SerializeToElement(finalControl, Json)), "The later real terminal source has no equivalent durable consumption proof.");
        }
        Require(childRoot.ProducedControl is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(childRoot.ProducedControl, Json), JsonSerializer.SerializeToElement(finalControl.Batch, Json)) && childRoot.ProducedControlSourceCommitId == (completedPair ? null : control.SourceCommitId), "Actual root emission or anchor differs from the cut.");
        if (completedPair) Require(call.ChildControl is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(call.ChildControl, Json), JsonSerializer.SerializeToElement(finalControl, Json)) && call.Completion?.OutcomeKey == "Completed" && call.Fault is null && !string.IsNullOrEmpty(call.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)), "Actual terminal call lost its original truthful ordinary completion receipt.");
        else Require(call.ChildControl is null, "Actual nonterminal notice fabricated ordinary completion.");
        var warning = Exact(parentStates, item.GetProperty("parent"), "node-warning"); var normals = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-normal").ToArray();
        Require(warning.Status == (phase == "finished" ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended) && normals.Length == (completedPair ? 1 : 0) && normals.All(state => state.Status == ActivityExecutionStatus.Completed), "Actual accepted caller branches differ or repeated.");
        var parentIncidents = await incidents.ListAsync(parentId); var childIncidents = await incidents.ListAsync(childId);
        Require(parentIncidents.Count == 0 && childIncidents.Count == 0 && (await poison.ListAsync(parentId)).Count == 0 && (await poison.ListAsync(childId)).Count == 0, "An actual source retry acquired incidents or poison.");
        Require(sources.Where(candidate => candidate.OutboxItemId != source.OutboxItemId).All(candidate => candidate.Status == RuntimePostCommitOutboxStatus.Delivered), "An unrelated actual source remains owed at the cut.");
        Require(sources.All(candidate => candidate.Intent.Kind != CancelKind), "The actual noninterrupting pair staged an unexpected child cancellation.");
        var physical = new List<BookmarkStateObservation>();
        foreach (var id in new[] { parentId, childId }) foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id)) physical.Add(await bookmarks.ObserveAsync(id, wait.BookmarkId) ?? throw new InvalidDataException("An actual physical wait disappeared."));
        Require(physical.Count == (phase == "finished" ? 0 : terminal ? 1 : 3) && physical.All(wait => wait.Scope == new PersistenceScope(scopeName) && wait.Generation > 0), "Actual live physical wait scope/count/generation differs.");
        foreach (var wait in physical)
        {
            var state = wait.State;
            var isWarning = state.WorkflowExecutionId == parentId && state.ActivityExecutionId == warning.InvocationId && state.ExecutableNodeId == "node-warning";
            var isCall = state.WorkflowExecutionId == parentId && state.ActivityExecutionId == call.InvocationId && state.ExecutableNodeId == "node-call";
            var isFinish = state.WorkflowExecutionId == childId && state.ActivityExecutionId == childStates.Single(candidate => candidate.Execution.ExecutableNodeId == "node-child-finish").InvocationId && state.ExecutableNodeId == "node-child-finish";
            Require(isWarning || !terminal && (isCall || isFinish), "A physical cut wait belongs to another invocation.");
            if (isWarning || isFinish) Require(state.Metadata.GetValueOrDefault("runtime.correlationId") == state.WorkflowExecutionId, "The actual retained Event correlation changed.");
        }
        var facts = new { Parent = parent, Child = child, ParentActivities = parentStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), ChildActivities = childStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), Dispatch = dispatch,
            ParentIncidents = parentIncidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), ChildIncidents = childIncidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), PhysicalBookmarks = physical.OrderBy(wait => wait.State.BookmarkId, StringComparer.Ordinal),
            OtherSources = sources.Where(candidate => candidate.OutboxItemId != source.OutboxItemId).OrderBy(candidate => candidate.OutboxItemId, StringComparer.Ordinal) };
        return new { Facts = facts, Source = source, SourceIntentFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent), ExactCommittedAcceptanceVerified = true, ExactPreAcknowledgmentSourceVerified = true };
    }
}
