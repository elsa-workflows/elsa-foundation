using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>Read-only, checked provider observations of the two actually captured legacy call pairs.</summary>
internal static class CalledSnapshotReader
{
    private const string NotificationKind = "Elsa.Activities.DispatchWorkflow.NotifyControl";
    private const string ResumeKind = WorkflowDispatchLifecycle.ResumeParentIntentKind;
    private const string OrdinaryFaultMessage = "Actual legacy called child ordinary fault.";

    internal static async Task<IReadOnlyList<object>> ReadAsync(string snapshot, string scopeName, string capturePath, string phase)
    {
        Require(phase is "captured" or "resumed", "Unknown called snapshot phase.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var capture = document.RootElement;
        Require(capture.GetProperty("passed").GetBoolean() && capture.GetProperty("phase").GetString() == "Capture", "An actual complete capture receipt is required.");
        var cases = capture.GetProperty("cases").EnumerateArray().ToArray();
        Require(cases.Length == 2 && cases.Count(item => item.GetProperty("faultChild").GetBoolean()) == 1, "Both legacy outcomes are required.");
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName));
        var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
        var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
        var incidents = new EfIncidentStateStore(context, access);
        var bookmarks = new EfBookmarkStateStore(context, access);
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var executables = new EfWorkflowExecutableStore(context, access);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName, "Unexpected source scope.");
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            var physicalId = RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity);
            Require(row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, physicalId), "An enumerated physical source differs from its checked identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("The checked source disappeared."));
        }
        Require(sources.All(item => item.Status == RuntimePostCommitOutboxStatus.Delivered), "A captured or finished host still owes an outbox result.");
        Require(sources.All(item => item.Intent.Kind != NotificationKind), "A legacy call inferred notification authority.");
        var observed = new List<object>();
        foreach (var item in cases)
        {
            var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!;
            var childId = item.GetProperty("childWorkflowExecutionId").GetString()!;
            var fault = item.GetProperty("faultChild").GetBoolean();
            var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("Captured caller missing.");
            var child = await workflows.FindAsync(childId) ?? throw new InvalidDataException("Captured child missing.");
            MatchWorkflow(parent, item.GetProperty("parentAtCapture")); MatchWorkflow(child, item.GetProperty("childAtCapture"));
            Require(parent.ParentWorkflowExecutionId is null && child.ParentWorkflowExecutionId == parentId, "Captured workflow ancestry changed.");
            Require(!child.SystemMetadata.ContainsKey(RuntimeMetadataKeys.ChildControlDispatchId), "The legacy child acquired a control-boundary dispatch hint.");
            var parentStates = await activities.ListAllAsync(parentId); var childStates = await activities.ListAllAsync(childId);
            var parentRoot = Exact(parentStates, item.GetProperty("parentAtCapture"), "parent-bpmn");
            var call = Exact(parentStates, item.GetProperty("parentAtCapture"), "node-call");
            var childRoot = Exact(childStates, item.GetProperty("childAtCapture"), "child-bpmn");
            var wait = Exact(childStates, item.GetProperty("childAtCapture"), "node-child-wait");
            Require(parentRoot.ParentActivityExecutionId is null && childRoot.ParentActivityExecutionId is null &&
                call.ParentActivityExecutionId == parentRoot.InvocationId && wait.ParentActivityExecutionId == childRoot.InvocationId,
                "The exact local call/root/wait ancestry changed.");
            var linked = (await dispatches.ListAsync(parentId)).Single();
            Require(linked.ParentActivityExecutionId == call.InvocationId && linked.ChildWorkflowExecutionId == childId &&
                linked.ParentWorkflowExecutionId == parentId && linked.Mode == WorkflowDispatchMode.WaitForCompletion &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(linked.ChildExecutable, child.PinnedExecutable), "The authoritative dispatch no longer links the exact captured pair.");
            NoCapability(linked.Metadata); NoCapability(call.Provenance.Metadata);
            var executable = await executables.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Original caller material missing.");
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable), "Caller material identity changed.");
            NoCapability(executable.CompatibilityMetadata);
            foreach (var node in executable.NodesById.Values) NoCapability(node.Metadata);
            Require(call.ChildControl is null && call.ChildControlInbox is null && childRoot.ProducedControl is null && childRoot.ProducedControlSourceCommitId is null,
                "The old dispatch or child inferred control projections.");
            var parentIncidents = await incidents.ListAsync(parentId); var childIncidents = await incidents.ListAsync(childId);
            var physical = new List<BookmarkStateObservation>();
            foreach (var id in new[] { parentId, childId })
                foreach (var bookmark in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id))
                    physical.Add(await bookmarks.ObserveAsync(id, bookmark.BookmarkId) ?? throw new InvalidDataException("A physical wait disappeared."));
            if (phase == "captured")
            {
                Require(parent.Status == WorkflowExecutionStatus.Running && child.Status == WorkflowExecutionStatus.Running &&
                    parentRoot.Status == ActivityExecutionStatus.Running && childRoot.Status == ActivityExecutionStatus.Running &&
                    call.Status == ActivityExecutionStatus.Suspended && wait.Status == ActivityExecutionStatus.Suspended &&
                    linked.Status == WorkflowDispatchStatus.Started && parentRoot.PrivateState?.StateVersion == 1 && childRoot.PrivateState?.StateVersion == 1,
                    "The actual old call pair is not at its original v1 wait checkpoint.");
                Require(physical.Count == 2 && parentIncidents.Count == 0 && childIncidents.Count == 0, "Old capture has missing waits or incidents.");
                Require(physical.Count(observation => observation.State.WorkflowExecutionId == parentId && observation.State.ActivityExecutionId == call.InvocationId && observation.State.ExecutableNodeId == "node-call") == 1 &&
                    physical.Count(observation => observation.State.WorkflowExecutionId == childId && observation.State.ActivityExecutionId == wait.InvocationId && observation.State.ExecutableNodeId == "node-child-wait") == 1 &&
                    physical.All(observation => observation.Generation > 0 && observation.Scope == new PersistenceScope(scopeName)), "The physical waits do not belong to the exact captured call/Event and scope.");
                Require(!childStates.Any(state => state.Execution.ExecutableNodeId == "node-child-tail"), "The child tail ran before capture.");
            }
            else
            {
                Require(parent.Status == WorkflowExecutionStatus.Completed && parentRoot.Status == ActivityExecutionStatus.Completed &&
                    call.Status == ActivityExecutionStatus.Completed && wait.Status == ActivityExecutionStatus.Completed &&
                    child.Status == (fault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed) &&
                    linked.Status == (fault ? WorkflowDispatchStatus.Faulted : WorkflowDispatchStatus.Completed) &&
                    physical.Count == 0 && parentIncidents.Count == 0 && call.Fault is null,
                    "Actual legacy ordinary-result completion or child truth differs.");
                Require(call.Completion?.OutcomeKey == (fault ? "Faulted" : "Completed"), "Legacy call result outcome changed.");
                var tail = childStates.Single(state => state.Execution.ExecutableNodeId == "node-child-tail");
                Require(tail.ParentActivityExecutionId == childRoot.InvocationId && tail.Status == (fault ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Completed), "The original child did not execute its intended tail.");
                if (fault)
                {
                    Require(tail.Fault?.Code == "workflow.fault" && tail.Fault.Message == OrdinaryFaultMessage, "The real Fault tail's code/message were replaced by an upgrade failure.");
                    var originalIncident = childIncidents.Single(incident => incident.ActivityExecutionId == tail.InvocationId && incident.FailureType == "ActivityReturnedFault");
                    Require(originalIncident.IsBlocking && originalIncident.Message == OrdinaryFaultMessage && originalIncident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.FaultWorkflow,
                        "The original child's exact blocking returned-fault incident was absorbed or changed.");
                }
                else Require(childIncidents.Count == 0, "Normal child gained an incident.");
                var resumes = sources.Where(source => source.Intent.Kind == ResumeKind && source.Intent.WorkflowExecutionId == parentId).ToArray();
                Require(resumes.Length == 1 && resumes[0].Intent.Payload is not null, "The exact ordinary parent resume source is missing or duplicated.");
                var payload = resumes[0].Intent.Payload!.Value;
                Require(payload.GetProperty("dispatchId").GetString() == linked.DispatchId &&
                    payload.GetProperty("parentActivityExecutionId").GetString() == call.InvocationId &&
                    payload.GetProperty("childWorkflowExecutionId").GetString() == childId, "Ordinary result source targets a different call.");
                var result = payload.GetProperty("result");
                Require(!result.TryGetProperty("control", out var control) || control.ValueKind == JsonValueKind.Null, "An upgraded legacy terminal result inferred control authority.");
            }
            observed.Add(new { Name = item.GetProperty("name").GetString(), Phase = phase, Parent = parent, Child = child,
                ParentActivities = parentStates, ChildActivities = childStates, Dispatch = linked,
                ParentIncidents = parentIncidents, ChildIncidents = childIncidents, PhysicalBookmarks = physical,
                LegacyCapabilityAbsent = true, CallerPinnedConsumerDeclarationsAbsent = true, ControlProjectionsAbsent = true });
        }
        return observed;
    }

    private static void MatchWorkflow(WorkflowExecutionState state, JsonElement capture)
    {
        var summary = capture.GetProperty("instance"); var pin = state.PinnedExecutable;
        Require(state.WorkflowExecutionId == summary.GetProperty("workflowExecutionId").GetString() &&
            pin.ArtifactId == summary.GetProperty("artifactId").GetString() && pin.DefinitionId == summary.GetProperty("definitionId").GetString() &&
            pin.DefinitionVersionId == summary.GetProperty("definitionVersionId").GetString() && pin.ArtifactVersion == summary.GetProperty("artifactVersion").GetString() &&
            pin.ArtifactHash == summary.GetProperty("artifactHash").GetString() &&
            state.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == capture.GetProperty("persistenceMode").GetString(), "Captured workflow pin or persistence mode changed.");
    }
    private static ActivityExecutionState Exact(IReadOnlyCollection<ActivityExecutionState> states, JsonElement capture, string node)
    {
        var prior = capture.GetProperty("activities").EnumerateArray().Single(item => item.GetProperty("executableNodeId").GetString() == node);
        var state = states.Single(item => item.Execution.ExecutableNodeId == node);
        Require(state.InvocationId == prior.GetProperty("activityExecutionId").GetString() &&
            state.Execution.WorkflowExecutionId == prior.GetProperty("workflowExecutionId").GetString() &&
            state.Execution.ActivityTypeVersion == prior.GetProperty("activityTypeVersion").GetString(), "An original captured invocation or type pin was replaced.");
        return state;
    }
    private static void NoCapability(IReadOnlyDictionary<string, string> metadata) => Require(
        !metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) && !metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion),
        "An actual legacy declaration acquired control capability.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
