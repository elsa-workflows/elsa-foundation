using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
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
    private const string NotifyKind = "Elsa.Activities.DispatchWorkflow.NotifyControl";
    private const string CancelKind = "Elsa.Activities.DispatchWorkflow.CancelChild";

    private static async Task<IReadOnlyList<object>> ReadEscalationsAsync(string snapshot, string scopeName, string receiptPath, string phase)
    {
        Require(phase is "escalation-parked" or "escalation-recovered" or "escalation-finished", "Unknown escalation observation phase.");
        var finished = phase == "escalation-finished";
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath));
        var receipt = document.RootElement;
        var expectedReceipt = phase switch { "escalation-parked" => "PrepareEscalations", "escalation-recovered" => "ObserveEscalations", _ => "FinishEscalations" };
        Require(receipt.GetProperty("passed").GetBoolean() && receipt.GetProperty("phase").GetString() == expectedReceipt, "Actual public escalation phase receipt required.");
        var cases = receipt.GetProperty("cases").EnumerateArray().ToArray();
        Require(cases.Length == 6 && cases.Select(item => item.GetProperty("kind").GetString() + ":" + item.GetProperty("interrupting").GetBoolean()).Distinct().Count() == 6 &&
            cases.All(item => item.GetProperty("kind").GetString() is "RunningEscalation" or "TerminalEscalation" or "SignalAndFailure"), "All six exact authored escalation policies required.");
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var query = connection.CreateCommand()) { query.CommandText = "PRAGMA query_only=ON"; await query.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName));
        var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
        var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
        var incidents = new EfIncidentStateStore(context, access);
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var bookmarks = new EfBookmarkStateStore(context, access);
        var material = new EfWorkflowExecutableStore(context, access);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName && row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)),
                "An enumerated physical source differs from its exact scoped identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("The checked source disappeared."));
        }
        var results = new List<object>();
        foreach (var item in cases)
        {
            var kind = item.GetProperty("kind").GetString()!; var interrupting = item.GetProperty("interrupting").GetBoolean();
            var failure = kind == "SignalAndFailure"; var terminal = kind != "RunningEscalation";
            var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!;
            var childId = item.GetProperty("childWorkflowExecutionId").GetString()!;
            var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("Actual caller missing.");
            var child = await workflows.FindAsync(childId) ?? throw new InvalidDataException("Actual child missing.");
            MatchPin(parent, item.GetProperty("parent").GetProperty("instance")); MatchPin(child, item.GetProperty("child").GetProperty("instance"));
            Require(parent.ParentWorkflowExecutionId is null && child.ParentWorkflowExecutionId == parentId &&
                parent.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced" && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced", "Actual ancestry or configured persistence changed.");
            var parentStates = await activities.ListAllAsync(parentId); var childStates = await activities.ListAllAsync(childId);
            var parentRoot = Exact(parentStates, item.GetProperty("parent"), "parent-bpmn");
            var childRoot = Exact(childStates, item.GetProperty("child"), "child-bpmn");
            var call = Exact(parentStates, item.GetProperty("parent"), "node-call");
            var dispatch = (await dispatches.ListAsync(parentId)).Single();
            Require(call.ParentActivityExecutionId == parentRoot.InvocationId && call.Provenance.ParentActivityExecutionId == parentRoot.InvocationId && parentRoot.ParentActivityExecutionId is null && childRoot.ParentActivityExecutionId is null &&
                dispatch.ParentWorkflowExecutionId == parentId && dispatch.ParentActivityExecutionId == call.InvocationId && dispatch.ChildWorkflowExecutionId == childId && dispatch.Mode == WorkflowDispatchMode.WaitForCompletion &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dispatch.ChildExecutable, child.PinnedExecutable) && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlDispatchId) == dispatch.DispatchId,
                "Actual call/root/pinned-child dispatch boundary differs.");
            Declares(dispatch.Metadata); Declares(call.Provenance.Metadata);
            var executable = await material.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Pinned caller missing.");
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable), "Pinned caller material changed.");
            Declares(executable.NodesById[parentRoot.Execution.ExecutableNodeId].Metadata);
            var expectedChild = failure ? WorkflowExecutionStatus.Faulted : terminal || finished && !interrupting ? WorkflowExecutionStatus.Completed : interrupting ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Running;
            var expectedParent = failure && !interrupting ? WorkflowExecutionStatus.Faulted : finished ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running;
            var expectedCall = terminal || finished && !interrupting ? ActivityExecutionStatus.Completed : interrupting ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Suspended;
            Require(child.Status == expectedChild && parent.Status == expectedParent && call.Status == expectedCall &&
                dispatch.Status == (failure ? WorkflowDispatchStatus.Faulted : terminal || finished && !interrupting ? WorkflowDispatchStatus.Completed : interrupting ? WorkflowDispatchStatus.Cancelled : WorkflowDispatchStatus.Started),
                "Actual escalation statuses or truthful dispatch state differ.");
            var ownedSources = sources.Where(source => source.Intent.Kind == NotifyKind && source.Intent.Payload!.Value.GetProperty("dispatchId").GetString() == dispatch.DispatchId ||
                source.Intent.Kind is WorkflowDispatchLifecycle.ResumeParentIntentKind or CancelKind && source.Intent.Payload!.Value.GetProperty("dispatchId").GetString() == dispatch.DispatchId).OrderBy(source => source.OutboxItemId, StringComparer.Ordinal).ToArray();
            Require(ownedSources.Length >= 1 && ownedSources.All(source => source.Status == RuntimePostCommitOutboxStatus.Delivered), "Actual control source remains owed or absent.");
            var batches = new List<RuntimeChildControlTransport>();
            foreach (var source in ownedSources.Where(source => source.Intent.Kind != CancelKind))
            {
                var payload = source.Intent.Payload!.Value;
                var transported = source.Intent.Kind == NotifyKind ? payload.Deserialize<RuntimeChildControlTransport>(Json) : payload.GetProperty("result").GetProperty("control").Deserialize<RuntimeChildControlTransport>(Json);
                // A canceled child may truthfully produce an ordinary terminal result after the caller's terminal cut.
                if (transported is null) continue;
                Require(source.OutboxItemId == transported.SourceOutboxItemId && source.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(transported.SourceCommitId, transported.SourceIntentId) &&
                    source.Intent.IntentId == transported.SourceIntentId && transported.Batch.ConsumerKey == ConsumerKey && transported.Batch.ConsumerVersion == 1 && source.Intent.WorkflowExecutionId == (source.Intent.Kind == NotifyKind ? childId : parentId) && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(transported.ParentExecutable, parent.PinnedExecutable) && transported.DispatchId == dispatch.DispatchId &&
                    transported.CallingActivityExecutionId == call.InvocationId && transported.ConsumerActivityExecutionId == parentRoot.InvocationId && transported.Batch.ChildWorkflowExecutionId == childId && transported.Batch.RootActivityExecutionId == childRoot.InvocationId,
                    "An actual immutable source has a foreign transport address or source identity.");
                batches.Add(transported);
            }
            var expectedOrdinals = kind == "RunningEscalation" && (finished || interrupting) ? new long[] { 0, 1 } : [0L];
            Require(batches.Select(batch => batch.Batch.Ordinal).Order().SequenceEqual(expectedOrdinals), "The actual stream lost, duplicated or fabricated an ordinal.");
            var produced = childRoot.ProducedControl ?? throw new InvalidDataException("Actual child root lost its emitted control.");
            var producedSource = batches.Single(batch => batch.Batch.Ordinal == produced.Ordinal);
            Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(produced, Json), JsonSerializer.SerializeToElement(producedSource.Batch, Json)) && childRoot.ProducedControlSourceCommitId == (produced.IsTerminal ? null : producedSource.SourceCommitId),
                "The actual child root emission differs from its immutable source or commit anchor.");
            foreach (var later in batches.Where(batch => batch.Batch.Ordinal == 1))
                Require(later.Batch.IsTerminal && later.Batch.Notifications.Count == 0 && later.Batch.AuthoredFailureCode is null, "The actual follow-up terminal batch fabricated a repeated signal or authored failure.");
            var control = batches.Single(batch => batch.Batch.Ordinal == (finished && !terminal && !interrupting ? 1 : 0));
            if (terminal || finished && !interrupting) Require(call.ChildControl is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(call.ChildControl, Json), JsonSerializer.SerializeToElement(control, Json)), "Actual ordinary result and source transport disagree.");
            else Require(call.ChildControl is null, "A nonterminal notification fabricated ordinary completion control.");
            var inbox = call.ChildControlInbox ?? throw new InvalidDataException("Actual caller lost its inbox.");
            var advanced = finished && !terminal && !interrupting;
            Require(control.DispatchId == dispatch.DispatchId && control.CallingActivityExecutionId == call.InvocationId && control.ParentWorkflowExecutionId == parentId && control.ConsumerActivityExecutionId == parentRoot.InvocationId &&
                control.Batch.ChildWorkflowExecutionId == childId && control.Batch.RootActivityExecutionId == childRoot.InvocationId && control.Batch.ConsumerKey == ConsumerKey && control.Batch.ConsumerVersion == 1 &&
                control.Batch.Ordinal == (advanced ? 1 : 0) && control.Batch.IsTerminal == (terminal || advanced) && control.Batch.AuthoredFailureCode == (failure ? "OTHER" : null) &&
                inbox.NextOrdinal == (advanced ? 2 : 1) && inbox.IsTerminalConsumed == (terminal || advanced) && inbox.Pending.Count == 0 && inbox.HasAccepted(control) && inbox.Target.MatchesAddress(control),
                "Actual escalation boundary or ordered consumed inbox differs.");
            if (advanced) Require(control.Batch.Notifications.Count == 0, "The completion fabricated a second escalation.");
            else AssertWarning(control.Batch);
            var warningBatch = batches.Single(batch => batch.Batch.Ordinal == 0);
            AssertWarning(warningBatch.Batch);
            Require(warningBatch.Batch.IsTerminal == terminal && warningBatch.Batch.AuthoredFailureCode == (failure ? "OTHER" : null), "Actual terminal signal/failure was split or reclassified.");
            Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(control, Json), JsonSerializer.SerializeToElement(batches.Single(batch => batch.Batch.Ordinal == control.Batch.Ordinal), Json)), "Latest actual inbox control differs from its immutable source.");
            if (advanced) Require(batches.Any(batch => batch.Batch.Ordinal == 1 && batch.Batch.IsTerminal && batch.Batch.Notifications.Count == 0 && batch.Batch.AuthoredFailureCode is null), "Fresh actual completion did not produce empty terminal one.");
            var cancellations = ownedSources.Where(source => source.Intent.Kind == CancelKind).ToArray();
            Require(cancellations.Length == (interrupting && !terminal ? 1 : 0), "Actual adapter canceled a terminal child or failed to cancel its running child.");
            foreach (var source in cancellations)
            {
                var payload = source.Intent.Payload!.Value;
                Require(source.Intent.WorkflowExecutionId == parentId && payload.GetProperty("childWorkflowExecutionId").GetString() == childId && payload.GetProperty("parentActivityExecutionId").GetString() == call.InvocationId, "Actual cancellation targeted a different child.");
            }
            var warnings = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-warning").ToArray();
            var normals = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-normal").ToArray();
            Require(warnings.Length == (failure && !interrupting ? 0 : 1) && warnings.All(state => state.Status == (finished ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended)) &&
                normals.Length == (!interrupting && !failure && (terminal || finished) ? 1 : 0) && normals.All(state => state.Status == ActivityExecutionStatus.Completed), "Actual boundary started duplicate or forbidden branches.");
            var parentIncidents = await incidents.ListAsync(parentId); var childIncidents = await incidents.ListAsync(childId);
            Require(parentIncidents.Count == (failure && !interrupting ? 1 : 0) && childIncidents.Count == (failure ? 1 : 0), "Actual control changed truthful incident ownership.");
            if (failure)
            {
                Require(childRoot.Status == ActivityExecutionStatus.Faulted && childRoot.Fault?.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey) == "bpmn.error" && childRoot.Fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == "OTHER" &&
                    childIncidents.Single().ActivityExecutionId == childRoot.InvocationId && childIncidents.Single().IsBlocking && childIncidents.Single().FailureType == "ActivityReturnedFault" && childIncidents.Single().ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.FaultWorkflow &&
                    call.Status == ActivityExecutionStatus.Completed && call.Completion?.OutcomeKey == "Faulted" && call.Fault is null, "Actual terminal authored failure was absorbed or changed.");
                if (!interrupting) Require(parentRoot.Fault?.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == "OTHER", "Actual uncaught terminal authored failure was reclassified.");
            }
            else if (call.Status == ActivityExecutionStatus.Completed) Require(call.Completion?.OutcomeKey == "Completed" && call.Fault is null, "Actual terminal escalation made a successful child fault.");
            if (terminal || advanced) Require(!string.IsNullOrEmpty(call.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)), "Actual ordinary terminal leaf lost its completion receipt.");
            var physical = new List<BookmarkStateObservation>();
            foreach (var id in new[] { parentId, childId })
                foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id))
                    physical.Add(await bookmarks.ObserveAsync(id, wait.BookmarkId) ?? throw new InvalidDataException("An exact physical wait disappeared."));
            Require(physical.Count == (finished || failure && !interrupting ? 0 : 1 + (!terminal && !interrupting ? 2 : 0)) &&
                physical.All(wait => wait.Scope == new PersistenceScope(scopeName) && wait.Generation > 0), "Actual physical wait count, generation or scope differs.");
            foreach (var wait in physical)
            {
                var state = wait.State;
                var isCallerWarning = state.WorkflowExecutionId == parentId && state.ActivityExecutionId == warnings.Single().InvocationId && state.ExecutableNodeId == "node-warning";
                var isCall = state.WorkflowExecutionId == parentId && state.ActivityExecutionId == call.InvocationId && state.ExecutableNodeId == "node-call";
                var isChildWait = state.WorkflowExecutionId == childId && state.ActivityExecutionId == childStates.Single(candidate => candidate.Execution.ExecutableNodeId == "node-child-finish").InvocationId && state.ExecutableNodeId == "node-child-finish";
                Require(isCallerWarning || !terminal && !interrupting && (isCall || isChildWait), "An actual physical wait belongs to a foreign call or child.");
                if (isCallerWarning || isChildWait) Require(state.Metadata.GetValueOrDefault("runtime.correlationId") == state.WorkflowExecutionId, "Actual pure instance correlation was not carried into the Event bookmark.");
            }
            results.Add(new { Kind = kind, Interrupting = interrupting, Parent = parent, Child = child,
                ParentActivities = parentStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), ChildActivities = childStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), Dispatch = dispatch,
                ParentIncidents = parentIncidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), ChildIncidents = childIncidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal),
                PhysicalBookmarks = physical.OrderBy(wait => wait.State.BookmarkId, StringComparer.Ordinal), Sources = ownedSources, ExactCapabilityAndInboxVerified = true, TruthfulChildAndCancellationVerified = true });
        }
        return results;
    }

    private static void AssertWarning(RuntimeChildControlBatch batch) => Require(batch.Notifications.Count == 1 && batch.Notifications[0].Code == "bpmn.escalation" &&
        batch.Notifications[0].Payload?.GetProperty("code").GetString() == "WARN", "Actual escalation notice code or payload changed.");
}
