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
    private static async Task<IReadOnlyList<object>> ReadConcurrentAsync(string snapshot, string scopeName, string receiptPath, string phase)
    {
        Require(phase is "concurrent-prepared" or "concurrent-selected" or "concurrent-recovered" or "concurrent-finished", "Unknown concurrent observation phase.");
        var handled = phase == "concurrent-prepared" ? 0 : phase == "concurrent-finished" ? 2 : 1;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath));
        var receipt = document.RootElement;
        var expectedReceipt = phase switch { "concurrent-prepared" => "Prepare", "concurrent-selected" => "HandleSelected", "concurrent-recovered" => "ObserveSelected", _ => "HandleOther" };
        Require(receipt.GetProperty("passed").GetBoolean() && receipt.GetProperty("phase").GetString() == expectedReceipt, "Actual concurrent public phase required.");
        var cases = receipt.GetProperty("cases").EnumerateArray().ToArray();
        Require(cases.Length == 2 && cases.Select(item => item.GetProperty("interrupting").GetBoolean()).Distinct().Count() == 2, "Both actual concurrent boundary policies required.");
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
        var poison = new EfWorkflowSchedulerPoisonStore(context, access);
        var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName && row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)), "An enumerated physical source differs from its exact scoped identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("Checked actual source disappeared."));
        }
        var results = new List<object>();
        foreach (var item in cases)
        {
            var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!;
            var interrupting = item.GetProperty("interrupting").GetBoolean();
            var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("Actual concurrent caller missing.");
            MatchPin(parent, item.GetProperty("parent").GetProperty("instance"));
            Require(parent.Status == (handled == 2 ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) && parent.ParentWorkflowExecutionId is null &&
                parent.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced", "Actual caller truth changed.");
            var parentStates = await activities.ListAllAsync(parentId);
            var root = Exact(parentStates, item.GetProperty("parent"), "parent-bpmn");
            var calls = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-call").ToArray();
            Require(calls.Length == 2 && calls.Select(call => call.InvocationId).Distinct().Count() == 2 && calls.Select(call => call.Provenance.ExecutionPathId).Distinct().Count() == 2 &&
                calls.All(call => call.IterationId is null && call.ParentActivityExecutionId == root.InvocationId && call.Provenance.ParentActivityExecutionId == root.InvocationId && call.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") == "call"),
                "Actual calls are not distinct null-iteration invocations of the same binding.");
            var links = await dispatches.ListAsync(parentId);
            Require(links.Count == 2 && links.Select(link => link.ChildWorkflowExecutionId).Distinct().Count() == 2 && links.All(link => link.Mode == WorkflowDispatchMode.WaitForCompletion && link.ParentWorkflowExecutionId == parentId && calls.Any(call => call.InvocationId == link.ParentActivityExecutionId)), "Actual caller has foreign or duplicate dispatches.");
            var executable = await material.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Pinned actual caller absent.");
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable), "Pinned caller material changed.");
            Declares(executable.NodesById[root.Execution.ExecutableNodeId].Metadata);
            var activeTokens = new List<string>();
            if (handled == 2) Require(root.PrivateState is null && root.Status == ActivityExecutionStatus.Completed, "Actual final caller retained semantic structural state.");
            else
            {
                Require(root.PrivateState?.StateVersion == 2 && root.Status == ActivityExecutionStatus.Running && root.PrivateState.Value.InlineValue is not null, "Actual concurrent caller lost its current semantic state.");
                activeTokens = root.PrivateState!.Value.InlineValue!.Value.GetProperty("activeWork").EnumerateArray()
                    .Where(work => work.GetProperty("nodeId").GetString() == "node-call").Select(work => work.GetProperty("tokenId").GetString()!).ToList();
                Require(activeTokens.Count == 2 - handled && activeTokens.Distinct().Count() == activeTokens.Count, "Actual live semantic call token count differs.");
            }
            ActivityExecutionState selectedCall; ActivityExecutionState otherCall;
            if (handled == 0)
            {
                selectedCall = calls.Single(call => call.Provenance.ExecutionPathId == activeTokens[1]);
                otherCall = calls.Single(call => call.Provenance.ExecutionPathId == activeTokens[0]);
                Require(selectedCall.InvocationId != otherCall.InvocationId && selectedCall.Provenance.ExecutionPathId != activeTokens[0], "The selector fell back to the first semantic token.");
            }
            else
            {
                var selection = item.GetProperty("selection");
                selectedCall = calls.Single(call => call.InvocationId == selection.GetProperty("selectedCallActivityExecutionId").GetString());
                otherCall = calls.Single(call => call.InvocationId == selection.GetProperty("otherCallActivityExecutionId").GetString());
                Require(selectedCall.Provenance.ExecutionPathId == selection.GetProperty("selectedSemanticTokenId").GetString() && otherCall.Provenance.ExecutionPathId == selection.GetProperty("otherSemanticTokenId").GetString() && selectedCall.InvocationId != otherCall.InvocationId, "Actual selected call/token correlation changed.");
                if (handled == 1) Require(activeTokens.Single() == otherCall.Provenance.ExecutionPathId, "The completed selected token remained live or retired its sibling.");
            }
            var childApis = item.GetProperty("children").EnumerateArray().ToArray();
            Require(childApis.Length == 2 && childApis.Select(api => api.GetProperty("instance").GetProperty("workflowExecutionId").GetString()).Distinct().Count() == 2, "Both actual child API observations required.");
            async Task<object> ObserveCallAsync(ActivityExecutionState call, bool done)
            {
                var link = links.Single(candidate => candidate.ParentActivityExecutionId == call.InvocationId);
                var child = await workflows.FindAsync(link.ChildWorkflowExecutionId) ?? throw new InvalidDataException("Actual linked sibling missing.");
                var api = childApis.Single(candidate => candidate.GetProperty("instance").GetProperty("workflowExecutionId").GetString() == child.WorkflowExecutionId);
                MatchPin(child, api.GetProperty("instance"));
                Require(child.ParentWorkflowExecutionId == parentId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(link.ChildExecutable, child.PinnedExecutable) &&
                    child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced" && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlDispatchId) == link.DispatchId &&
                    child.Status == (done ? interrupting ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) &&
                    link.Status == (done ? interrupting ? WorkflowDispatchStatus.Cancelled : WorkflowDispatchStatus.Completed : WorkflowDispatchStatus.Started) &&
                    call.Status == (done ? interrupting ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended), "Actual selected/sibling status, pin or boundary truth differs.");
                Declares(link.Metadata); Declares(call.Provenance.Metadata);
                var childStates = await activities.ListAllAsync(child.WorkflowExecutionId);
                var childRoot = Exact(childStates, api, "child-bpmn"); var emit = Exact(childStates, api, "node-child-emit");
                Require(childRoot.ParentActivityExecutionId is null && emit.ParentActivityExecutionId == childRoot.InvocationId, "Actual sibling root/Event ancestry changed.");
                var childIncidents = await incidents.ListAsync(child.WorkflowExecutionId); Require(childIncidents.Count == 0 && (await poison.ListAsync(child.WorkflowExecutionId)).Count == 0, "Actual sibling gained an incident or poison.");
                var startSource = sources.Single(source => source.Intent.Kind == WorkflowDispatchLifecycle.StartChildIntentKind && source.Intent.Payload!.Value.GetProperty("dispatchId").GetString() == link.DispatchId);
                var identity = new WorkflowDispatchIdentity(parentId, call.InvocationId);
                Require(startSource.Status == RuntimePostCommitOutboxStatus.Delivered && startSource.Intent.IntentId == identity.StartIntentId && startSource.Intent.IdempotencyKey == identity.StartIdempotencyKey &&
                    startSource.Intent.WorkflowExecutionId == parentId && startSource.Intent.ActivityExecutionId == call.InvocationId &&
                    startSource.Intent.Payload!.Value.GetProperty("childWorkflowExecutionId").GetString() == child.WorkflowExecutionId,
                    "Actual sibling lost its original immutable keyed start source.");
                var ownSources = sources.Where(source => source.Intent.Kind is NotifyKind or CancelKind or WorkflowDispatchLifecycle.ResumeParentIntentKind && source.Intent.Payload!.Value.GetProperty("dispatchId").GetString() == link.DispatchId).OrderBy(source => source.OutboxItemId, StringComparer.Ordinal).ToArray();
                Require(ownSources.All(source => source.Status == RuntimePostCommitOutboxStatus.Delivered) && ownSources.Length == (done ? interrupting ? 3 : 2 : 0), "Actual sibling source responsibilities changed or remain owed.");
                var physical = new List<BookmarkStateObservation>();
                foreach (var id in new[] { parentId, child.WorkflowExecutionId })
                    foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id))
                        if (id == child.WorkflowExecutionId || wait.ActivityExecutionId == call.InvocationId)
                            physical.Add(await bookmarks.ObserveAsync(id, wait.BookmarkId) ?? throw new InvalidDataException("Actual sibling physical wait disappeared."));
                Require(physical.Count == (done ? 0 : 2) && physical.All(wait => wait.Scope == new PersistenceScope(scopeName) && wait.Generation > 0), "Actual sibling lost its exact physical waits or retained terminal waits.");
                if (!done)
                {
                    Require(call.ChildControl is null && call.ChildControlInbox is null && childRoot.ProducedControl is null && emit.Status == ActivityExecutionStatus.Suspended &&
                        physical.Count(wait => wait.State.WorkflowExecutionId == parentId && wait.State.ActivityExecutionId == call.InvocationId && wait.State.ExecutableNodeId == "node-call") == 1 &&
                        physical.Count(wait => wait.State.WorkflowExecutionId == child.WorkflowExecutionId && wait.State.ActivityExecutionId == emit.InvocationId && wait.State.ExecutableNodeId == "node-child-emit" && wait.State.Metadata.GetValueOrDefault("runtime.correlationId") == child.WorkflowExecutionId) == 1,
                        "An untouched actual sibling advanced, acquired control or changed physical Event correlation.");
                }
                else
                {
                    var transports = new List<RuntimeChildControlTransport>();
                    foreach (var source in ownSources)
                    {
                        var payload = source.Intent.Payload!.Value;
                        if (source.Intent.Kind == CancelKind)
                        {
                            Require(interrupting && source.Intent.WorkflowExecutionId == parentId && payload.GetProperty("childWorkflowExecutionId").GetString() == child.WorkflowExecutionId && payload.GetProperty("parentActivityExecutionId").GetString() == call.InvocationId, "Actual cancellation targeted a sibling.");
                            continue;
                        }
                        var transport = (source.Intent.Kind == NotifyKind ? payload : payload.GetProperty("result").GetProperty("control")).Deserialize<RuntimeChildControlTransport>(Json) ?? throw new InvalidDataException("Actual source lost typed transport.");
                        Require(transport.DispatchId == link.DispatchId && transport.ParentWorkflowExecutionId == parentId && transport.CallingActivityExecutionId == call.InvocationId && transport.ConsumerActivityExecutionId == root.InvocationId &&
                            WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(transport.ParentExecutable, parent.PinnedExecutable) && transport.Batch.ChildWorkflowExecutionId == child.WorkflowExecutionId && transport.Batch.RootActivityExecutionId == childRoot.InvocationId &&
                            transport.Batch.ConsumerKey == ConsumerKey && transport.Batch.ConsumerVersion == 1 && source.OutboxItemId == transport.SourceOutboxItemId && source.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(transport.SourceCommitId, transport.SourceIntentId) &&
                            source.Intent.IntentId == transport.SourceIntentId && source.Intent.WorkflowExecutionId == (source.Intent.Kind == NotifyKind ? child.WorkflowExecutionId : parentId), "Actual same-binding control crossed its exact boundary or source identity.");
                        transports.Add(transport);
                    }
                    Require(transports.Count == 2 && transports.Select(transport => transport.Batch.Ordinal).Order().SequenceEqual([0L, 1L]), "Actual handled call lost ordered signal/terminal stream.");
                    var first = transports.Single(transport => transport.Batch.Ordinal == 0); var last = transports.Single(transport => transport.Batch.Ordinal == 1);
                    AssertWarning(first.Batch); Require(!first.Batch.IsTerminal && first.Batch.AuthoredFailureCode is null && last.Batch.IsTerminal && last.Batch.Notifications.Count == 0 && last.Batch.AuthoredFailureCode is null, "Actual handled sibling's batch meaning changed.");
                    Require(childRoot.ProducedControl is not null && childRoot.ProducedControlSourceCommitId is null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(childRoot.ProducedControl, Json), JsonSerializer.SerializeToElement(last.Batch, Json)), "Actual terminal child root differs from its last source.");
                    var inbox = call.ChildControlInbox ?? throw new InvalidDataException("Actual handled sibling has no inbox.");
                    Require(inbox.NextOrdinal == (interrupting ? 1 : 2) && inbox.IsTerminalConsumed == !interrupting && inbox.Pending.Count == 0 && inbox.HasAccepted(first) && inbox.Target.MatchesAddress(last), "Actual sibling consumed another call's stream or fabricated cancellation consumption.");
                    if (interrupting) Require(call.ChildControl is null && ownSources.Count(source => source.Intent.Kind == CancelKind) == 1, "Canceled sibling gained ordinary completion or lost its exact cancel.");
                    else Require(call.ChildControl is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(call.ChildControl, Json), JsonSerializer.SerializeToElement(last, Json)) && inbox.HasAccepted(last) && call.Completion?.OutcomeKey == "Completed" && call.Fault is null && !string.IsNullOrEmpty(call.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)), "Actual successful sibling lost truthful one-time completion.");
                }
                return new { Call = call, Dispatch = link, Child = child, Activities = childStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), Incidents = childIncidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), PhysicalBookmarks = physical.OrderBy(wait => wait.State.BookmarkId, StringComparer.Ordinal), StartSource = startSource, Sources = ownSources };
            }
            var selected = await ObserveCallAsync(selectedCall, handled >= 1); var other = await ObserveCallAsync(otherCall, handled == 2);
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(links.First().ChildExecutable, links.Last().ChildExecutable), "The actual sibling calls do not share one child pin.");
            if (handled > 0)
            {
                var selection = item.GetProperty("selection");
                Require(links.Single(link => link.ParentActivityExecutionId == selectedCall.InvocationId).ChildWorkflowExecutionId == selection.GetProperty("selectedChildWorkflowExecutionId").GetString() &&
                    links.Single(link => link.ParentActivityExecutionId == otherCall.InvocationId).ChildWorkflowExecutionId == selection.GetProperty("otherChildWorkflowExecutionId").GetString() &&
                    item.GetProperty("targetCallActivityExecutionId").GetString() == (handled == 1 ? selectedCall : otherCall).InvocationId && item.GetProperty("targetSemanticTokenId").GetString() == (handled == 1 ? selectedCall : otherCall).Provenance.ExecutionPathId,
                    "Public actual continuation targeted a different prepared call/token.");
            }
            var warnings = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-warning").ToArray(); var normals = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-normal").ToArray();
            var parentIncidents = await incidents.ListAsync(parentId);
            Require(warnings.Length == handled && warnings.All(state => state.Status == ActivityExecutionStatus.Completed) && normals.Length == (interrupting ? 0 : handled) && normals.All(state => state.Status == ActivityExecutionStatus.Completed) && parentIncidents.Count == 0 && (await poison.ListAsync(parentId)).Count == 0, "Actual parent branch count, incident or poison differs.");
            var allPhysical = new List<BookmarkStateObservation>();
            foreach (var id in new[] { parentId }.Concat(links.Select(link => link.ChildWorkflowExecutionId)))
                foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id)) allPhysical.Add(await bookmarks.ObserveAsync(id, wait.BookmarkId) ?? throw new InvalidDataException("Actual full physical observation lost wait."));
            Require(allPhysical.Count == 2 * (2 - handled), "Actual caller/siblings retained a foreign or missing wait.");
            results.Add(new { ParentWorkflowExecutionId = parentId, Interrupting = interrupting, Parent = parent, ParentActivities = parentStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), ParentIncidents = parentIncidents, Selected = selected, Other = other,
                ActiveCallTokens = activeTokens, PhysicalBookmarks = allPhysical.OrderBy(wait => wait.State.BookmarkId, StringComparer.Ordinal), ExactSameBindingSecondSemanticSelectionVerified = true });
        }
        return results;
    }
}
