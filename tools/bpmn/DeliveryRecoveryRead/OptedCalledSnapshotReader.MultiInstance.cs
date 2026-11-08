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
    internal static async Task<IReadOnlyList<object>> ReadMultiInstanceAsync(string snapshot, string scopeName, string apiPath, int completedCount)
    {
        Require(completedCount is >= 1 and <= 3, "Unknown actual multi-instance completion stage.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(apiPath)); var api = document.RootElement;
        Require(api.GetProperty("passed").GetBoolean() && api.GetProperty("completedCount").GetInt32() == completedCount, "Actual observed multi-instance stage required.");
        var cases = api.GetProperty("cases").EnumerateArray().ToArray();
        Require(cases.Length == 4 && cases.Select(item => item.GetProperty("persistenceMode").GetString() + ":" + item.GetProperty("isSequential").GetBoolean()).Distinct().Count() == 4,
            "All actual sequential/parallel and Balanced/Durable cases are required.");
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var query = connection.CreateCommand()) { query.CommandText = "PRAGMA query_only=ON"; await query.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName)); var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
        var activities = new EfActivityExecutionStateStore(context, access, new NoPaging()); var dispatches = new EfWorkflowDispatchStore(context, access);
        var bookmarks = new EfBookmarkStateStore(context, access); var incidents = new EfIncidentStateStore(context, access);
        var poison = new EfWorkflowSchedulerPoisonStore(context, access); var material = new EfWorkflowExecutableStore(context, access);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access); var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName && row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)), "An actual MI source has an invalid scoped physical identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("An actual MI source disappeared."));
        }
        var result = new List<object>();
        foreach (var item in cases)
        {
            var mode = item.GetProperty("persistenceMode").GetString()!; var sequential = item.GetProperty("isSequential").GetBoolean();
            var originalChildPin = item.GetProperty("originalChildPin").Deserialize<WorkflowExecutableIdentity>(Json) ?? throw new InvalidDataException("The captured MI child pin is absent.");
            Require(mode is "Balanced" or "Durable" && item.GetProperty("cardinality").GetInt32() == 3, "The actual MI mode/cardinality changed.");
            var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!;
            var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("The original MI caller is absent.");
            MatchPin(parent, item.GetProperty("parent").GetProperty("instance"));
            Require(parent.Status == (completedCount == 3 ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) && parent.ParentWorkflowExecutionId is null &&
                parent.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == mode, "The actual MI caller outcome or mode differs.");
            var parentStates = await activities.ListAllAsync(parentId); var root = Exact(parentStates, item.GetProperty("parent"), "mi-bpmn");
            var pinned = await material.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("The original MI material is absent.");
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(pinned.Identity, parent.PinnedExecutable), "The actual MI parent material changed.");
            var calls = parentStates.Where(state => state.Execution.ExecutableNodeId == "node-call").OrderBy(state => state.IterationId, StringComparer.Ordinal).ToArray();
            var expectedCalls = sequential ? Math.Min(3, completedCount + 1) : 3;
            Require(root.ParentActivityExecutionId is null && calls.Length == expectedCalls && calls.Select(call => call.IterationId).Distinct().Count() == expectedCalls &&
                calls.All(call => call.IterationId == "bpmn-mi:" + call.Provenance.ExecutionPathId && call.Provenance.IterationId == call.IterationId && call.Provenance.SchedulingCause == "multi-instance" &&
                    call.ParentActivityExecutionId == root.InvocationId && call.Provenance.ParentActivityExecutionId == root.InvocationId && call.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") == "mi-call") &&
                calls.Count(call => call.Status == ActivityExecutionStatus.Completed) == completedCount && calls.Count(call => call.Status == ActivityExecutionStatus.Suspended) == expectedCalls - completedCount,
                "The actual MI invocation/iteration/token identities or exact completed count differ.");
            var activeTokens = new List<string>();
            if (completedCount == 3) Require(root.Status == ActivityExecutionStatus.Completed && root.PrivateState is null, "The final MI caller retained semantic work.");
            else
            {
                Require(root.Status == ActivityExecutionStatus.Running && root.PrivateState?.StateVersion == 2 && root.PrivateState.Value.InlineValue is not null, "The actual MI caller lost its captured semantic state.");
                var state = root.PrivateState!.Value.InlineValue!.Value; var loops = state.GetProperty("loops").EnumerateArray().ToArray();
                Require(loops.Length == 1 && loops[0].GetProperty("loopId").GetString() == item.GetProperty("originalLoopId").GetString() && loops[0].GetProperty("elementId").GetString() == "mi-call" &&
                    loops[0].GetProperty("isSequential").GetBoolean() == sequential && loops[0].GetProperty("totalCount").GetInt32() == 3 &&
                    loops[0].GetProperty("nextIndex").GetInt32() == (sequential ? completedCount + 1 : 3) && loops[0].GetProperty("completedCount").GetInt32() == completedCount,
                    "The original MI loop counters, identity or sequential policy changed.");
                var work = state.GetProperty("activeWork").EnumerateArray().ToArray(); activeTokens = work.Select(work => work.GetProperty("tokenId").GetString()!).ToList();
                Require(work.Length == expectedCalls - completedCount && work.All(work => work.GetProperty("nodeId").GetString() == "node-call" && work.GetProperty("elementId").GetString() == "mi-call" &&
                    work.GetProperty("schedulingCause").GetString() == "multi-instance" && work.GetProperty("iterationId").GetString() == "bpmn-mi:" + work.GetProperty("tokenId").GetString()) &&
                    activeTokens.Order(StringComparer.Ordinal).SequenceEqual(calls.Where(call => call.Status == ActivityExecutionStatus.Suspended).Select(call => call.Provenance.ExecutionPathId!).Order(StringComparer.Ordinal)),
                    "The actual MI live semantic work does not match its held native iterations.");
            }
            var links = await dispatches.ListAsync(parentId); var childApis = item.GetProperty("children").EnumerateArray().ToArray();
            Require(links.Count == expectedCalls && childApis.Length == expectedCalls && links.Select(link => link.ChildWorkflowExecutionId).Distinct().Count() == expectedCalls, "The actual MI child set was lost or duplicated.");
            var observations = new List<object>(); var physicalAll = new List<BookmarkStateObservation>();
            foreach (var call in calls)
            {
                var done = call.Status == ActivityExecutionStatus.Completed; var link = links.Single(link => link.ParentActivityExecutionId == call.InvocationId);
                var child = await workflows.FindAsync(link.ChildWorkflowExecutionId) ?? throw new InvalidDataException("An actual MI child is absent.");
                var childApi = childApis.Single(api => api.GetProperty("instance").GetProperty("workflowExecutionId").GetString() == child.WorkflowExecutionId); MatchPin(child, childApi.GetProperty("instance"));
                Require(child.ParentWorkflowExecutionId == parentId && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == mode &&
                    child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlDispatchId) == link.DispatchId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(link.ChildExecutable, child.PinnedExecutable) && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(child.PinnedExecutable, originalChildPin) &&
                    link.ParentWorkflowExecutionId == parentId && link.Mode == WorkflowDispatchMode.WaitForCompletion && link.Status == (done ? WorkflowDispatchStatus.Completed : WorkflowDispatchStatus.Started) &&
                    child.Status == (done ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running), "An actual MI child/dispatch changed its full pin, ancestry, mode or outcome.");
                var childStates = await activities.ListAllAsync(child.WorkflowExecutionId); var childRoot = Exact(childStates, childApi, "child-wait");
                Require(childStates.Count == 1 && childRoot.ParentActivityExecutionId is null && childRoot.Status == (done ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Suspended), "An actual MI child Event repeated or changed.");
                var identity = new WorkflowDispatchIdentity(parentId, call.InvocationId);
                var ownSources = sources.Where(source => source.Intent.Kind is WorkflowDispatchLifecycle.StartChildIntentKind or WorkflowDispatchLifecycle.ResumeParentIntentKind && source.Intent.Payload!.Value.GetProperty("dispatchId").GetString() == link.DispatchId).ToArray();
                Require(ownSources.Length == (done ? 2 : 1) && ownSources.All(source => source.Status == RuntimePostCommitOutboxStatus.Delivered), "An actual MI start/result source is absent, repeated or owed.");
                var start = ownSources.Single(source => source.Intent.Kind == WorkflowDispatchLifecycle.StartChildIntentKind);
                Require(start.Intent.IntentId == identity.StartIntentId && start.Intent.IdempotencyKey == identity.StartIdempotencyKey && start.Intent.WorkflowExecutionId == parentId &&
                    start.Intent.ActivityExecutionId == call.InvocationId && start.Intent.Payload!.Value.GetProperty("childWorkflowExecutionId").GetString() == child.WorkflowExecutionId, "An actual MI child lost its original keyed admission source.");
                var physical = new List<BookmarkStateObservation>();
                foreach (var owner in new[] { parentId, child.WorkflowExecutionId }) foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(owner))
                    if (owner == child.WorkflowExecutionId || wait.ActivityExecutionId == call.InvocationId) physical.Add(await bookmarks.ObserveAsync(owner, wait.BookmarkId) ?? throw new InvalidDataException("An actual MI physical wait disappeared."));
                Require(physical.Count == (done ? 0 : 2) && physical.All(wait => wait.Scope == new PersistenceScope(scopeName) && wait.Generation > 0), "An actual MI iteration has missing, foreign or terminal waits.");
                if (done)
                {
                    var source = ownSources.Single(source => source.Intent.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind);
                    var transport = source.Intent.Payload!.Value.GetProperty("result").GetProperty("control").Deserialize<RuntimeChildControlTransport>(Json) ?? throw new InvalidDataException("An actual MI terminal source lost its typed control.");
                    Require(call.ChildControl is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(call.ChildControl, Json), JsonSerializer.SerializeToElement(transport, Json)) &&
                        transport.DispatchId == link.DispatchId && transport.ParentWorkflowExecutionId == parentId && transport.CallingActivityExecutionId == call.InvocationId && transport.ConsumerActivityExecutionId == root.InvocationId &&
                        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(transport.ParentExecutable, parent.PinnedExecutable) && transport.Batch.IsTerminal && transport.Batch.Ordinal == 0 && transport.Batch.Notifications.Count == 0 &&
                        transport.Batch.AuthoredFailureCode is null && transport.Batch.ConsumerKey == ConsumerKey && transport.Batch.ConsumerVersion == 1 && transport.Batch.ChildWorkflowExecutionId == child.WorkflowExecutionId && transport.Batch.RootActivityExecutionId == childRoot.InvocationId &&
                        source.OutboxItemId == transport.SourceOutboxItemId && source.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(transport.SourceCommitId, transport.SourceIntentId) && source.Intent.IntentId == transport.SourceIntentId &&
                        call.ChildControlInbox is { NextOrdinal: 1, IsTerminalConsumed: true } inbox && inbox.Pending.Count == 0 && inbox.HasAccepted(transport) && inbox.Target.MatchesAddress(transport) &&
                        call.Completion?.OutcomeKey == "Completed" && call.Fault is null && !string.IsNullOrEmpty(call.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)), "A delivered MI result lacks exact committed caller consumption.");
                }
                else Require(call.ChildControl is null && call.ChildControlInbox is null && call.Completion is null && call.Fault is null &&
                    physical.Count(wait => wait.State.WorkflowExecutionId == parentId && wait.State.ActivityExecutionId == call.InvocationId) == 1 &&
                    physical.Count(wait => wait.State.WorkflowExecutionId == child.WorkflowExecutionId && wait.State.ActivityExecutionId == childRoot.InvocationId && wait.State.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CorrelationId) == child.WorkflowExecutionId) == 1,
                    "A held actual MI iteration advanced or lost its exact Event correlation.");
                Require((await incidents.ListAsync(child.WorkflowExecutionId)).Count == 0 && (await poison.ListAsync(child.WorkflowExecutionId)).Count == 0, "An actual MI child gained incidents or poison.");
                physicalAll.AddRange(physical); observations.Add(new { Call = call, Dispatch = link, Child = child, ChildActivities = childStates, PhysicalBookmarks = physical, Sources = ownSources.OrderBy(source => source.OutboxItemId, StringComparer.Ordinal) });
            }
            Require((await incidents.ListAsync(parentId)).Count == 0 && (await poison.ListAsync(parentId)).Count == 0, "An actual MI parent gained incidents or poison.");
            Require(links.Count(link => link.ChildWorkflowExecutionId == item.GetProperty("completedChildWorkflowExecutionId").GetString()) == 1 &&
                calls.Single(call => call.InvocationId == links.Single(link => link.ChildWorkflowExecutionId == item.GetProperty("completedChildWorkflowExecutionId").GetString()).ParentActivityExecutionId).Status == ActivityExecutionStatus.Completed,
                "The original completed MI child was lost or re-opened.");
            result.Add(new { ParentWorkflowExecutionId = parentId, PersistenceMode = mode, IsSequential = sequential, CompletedCount = completedCount,
                Parent = parent, ParentActivities = parentStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal), ActiveCallTokens = activeTokens, Calls = observations,
                PhysicalBookmarks = physicalAll.OrderBy(wait => wait.State.BookmarkId, StringComparer.Ordinal), ExactLoopIterationAndConsumptionVerified = true });
        }
        return result;
    }
}
