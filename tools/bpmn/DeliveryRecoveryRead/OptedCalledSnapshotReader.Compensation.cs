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
    internal static async Task<object> ReadCompensationAsync(string snapshot, string scopeName, string apiPath, string stage)
    {
        Require(stage is "HandlerB" or "HandlerA" or "Settled" or "Finished", "Unknown compensation stage.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(apiPath));
        var api = document.RootElement;
        Require(api.GetProperty("passed").GetBoolean() && api.GetProperty("expectedStage").GetString() == stage,
            "Successful actual compensation stage required.");
        var item = api.GetProperty("case"); var view = api.GetProperty("workflow");
        var workflowId = item.GetProperty("workflowExecutionId").GetString()!;
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var query = connection.CreateCommand()) { query.CommandText = "PRAGMA query_only=ON"; await query.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName));
        var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
        var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
        var bookmarks = new EfBookmarkStateStore(context, access); var incidents = new EfIncidentStateStore(context, access);
        var dispatches = new EfWorkflowDispatchStore(context, access); var material = new EfWorkflowExecutableStore(context, access);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access); var poison = new EfWorkflowSchedulerPoisonStore(context, access);
        var workflow = await workflows.FindAsync(workflowId) ?? throw new InvalidDataException("Actual compensation workflow missing.");
        MatchPin(workflow, view.GetProperty("instance"));
        Require(workflow.ParentWorkflowExecutionId is null && workflow.Status == (stage == "Finished" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running) &&
            workflow.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced", "Actual compensation status/ancestry/mode changed.");
        var executable = await material.FindAsync(workflow.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Actual compensation pinned material missing.");
        Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, workflow.PinnedExecutable), "Actual compensation material changed.");
        var states = await activities.ListAllAsync(workflowId); var root = Exact(states, view, "compensation-bpmn");
        Require(root.ParentActivityExecutionId is null && root.Status == (stage == "Finished" ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Running), "Actual compensation root changed.");
        foreach (var id in new[] { "node-host-a", "node-host-b" })
        {
            var state = Exact(states, view, id);
            Require(state.Status == ActivityExecutionStatus.Completed && state.Completion?.OutcomeKey == "Done" && state.Fault is null &&
                state.ParentActivityExecutionId == root.InvocationId && state.Provenance.ParentActivityExecutionId == root.InvocationId && state.IterationId is null,
                "An ordinary bound task lost its exact successful completion.");
        }
        var b = Exact(states, view, "node-handler-b");
        Require(b.InvocationId == item.GetProperty("handlerBActivityExecutionId").GetString(), "The original compensation handler identity changed.");
        var expectedWait = stage switch { "HandlerB" => "node-handler-b", "HandlerA" => "node-handler-a", "Settled" => "node-hold", _ => null };
        foreach (var id in new[] { "node-handler-b", "node-handler-a", "node-hold" })
        {
            var expected = id switch
            {
                "node-handler-b" => stage == "HandlerB" ? ActivityExecutionStatus.Suspended : ActivityExecutionStatus.Completed,
                "node-handler-a" => stage == "HandlerB" ? (ActivityExecutionStatus?)null : stage == "HandlerA" ? ActivityExecutionStatus.Suspended : ActivityExecutionStatus.Completed,
                _ => stage is "HandlerB" or "HandlerA" ? (ActivityExecutionStatus?)null : stage == "Settled" ? ActivityExecutionStatus.Suspended : ActivityExecutionStatus.Completed
            };
            var matching = states.Where(state => state.Execution.ExecutableNodeId == id).ToArray();
            if (expected is null) Require(matching.Length == 0, "A later compensation handler or continuation ran early.");
            else
            {
                var state = Exact(states, view, id);
                Require(matching.Length == 1 && state.Status == expected && state.Fault is null && state.ParentActivityExecutionId == root.InvocationId &&
                    state.Provenance.ParentActivityExecutionId == root.InvocationId && state.IterationId is null &&
                    (expected != ActivityExecutionStatus.Completed || state.Completion?.OutcomeKey == "Done"), "Actual compensation handler ancestry/result/order changed.");
                if (id != "node-hold") Require(state.Provenance.SchedulingCause == "compensation" &&
                    state.Provenance.Metadata.GetValueOrDefault("bpmn.targetNodeId") == id &&
                    state.Provenance.Metadata.GetValueOrDefault("bpmn.elementId") == (id == "node-handler-b" ? "handler-b" : "handler-a"),
                    "Actual compensation handler lost its exact scheduling identity.");
            }
        }
        Require(states.Count == (stage == "HandlerB" ? 4 : stage == "HandlerA" ? 5 : 6), "Actual compensation executions repeated or unexpected work ran.");
        if (stage == "Finished") Require(root.PrivateState is null, "Finished compensation retained structural state.");
        else
        {
            Require(root.PrivateState?.StateVersion is 1 or 2 && root.PrivateState.Value.Type.Alias == "Elsa.Bpmn.ExecutionState" &&
                root.PrivateState.Value.Type.SchemaVersion == root.PrivateState.StateVersion && root.PrivateState.Value.InlineValue is not null,
                "Actual compensation semantic state missing or unsupported.");
            var state = root.PrivateState!.Value.InlineValue!.Value;
            var registrations = state.GetProperty("compensables").EnumerateArray().ToArray(); var runs = state.GetProperty("compensationRuns").EnumerateArray().ToArray();
            Require(registrations.Length == 2 && registrations[0].GetProperty("hostElementId").GetString() == "host-a" && registrations[0].GetProperty("handlerElementId").GetString() == "handler-a" &&
                registrations[1].GetProperty("hostElementId").GetString() == "host-b" && registrations[1].GetProperty("handlerElementId").GetString() == "handler-b" &&
                registrations.Select(item => item.GetProperty("compensableId").GetString()).Distinct().Count() == 2, "Actual ordinary-task registrations or handler association changed.");
            Require(registrations[0].GetProperty("status").GetInt32() == (stage == "Settled" ? 2 : 1) && registrations[1].GetProperty("status").GetInt32() == (stage == "HandlerB" ? 1 : 2), "Actual claimed/compensated lifecycle differs.");
            if (stage == "Settled") Require(runs.Length == 0, "Settled compensation retained a live replay run.");
            else
            {
                Require(runs.Length == 1, "Actual compensation replay run is missing or repeated.");
                var expectedIds = (stage == "HandlerB" ? registrations.Reverse() : registrations.Take(1)).Select(item => item.GetProperty("compensableId").GetString());
                Require(runs[0].GetProperty("pendingCompensableIds").EnumerateArray().Select(item => item.GetString()).SequenceEqual(expectedIds), "Actual replay does not preserve reverse registration order.");
            }
            var legacy = root.PrivateState.StateVersion == 1;
            Require(!state.TryGetProperty(legacy ? "activeWork" : "activeChildren", out _), "Actual state silently mixed legacy and current wire formats.");
            var work = state.GetProperty(legacy ? "activeChildren" : "activeWork").EnumerateArray().ToArray();
            Require(work.Length == 1 && work[0].GetProperty("nodeId").GetString() == expectedWait &&
                (stage == "Settled" || work[0].GetProperty("schedulingCause").GetString() == "compensation"), "Actual semantic live work is not the exact owed handler.");
            var active = states.Single(s => s.Execution.ExecutableNodeId == expectedWait);
            Require(active.Provenance.ExecutionPathId == work[0].GetProperty("tokenId").GetString() &&
                active.Provenance.SchedulingCause == work[0].GetProperty("schedulingCause").GetString(), "The actual handler execution is not its semantic token.");
        }
        var faults = await incidents.ListAsync(workflowId); var links = await dispatches.ListAsync(workflowId);
        Require(faults.Count == 0 && links.Count == 0 && (await poison.ListAsync(workflowId)).Count == 0, "Ordinary-task compensation acquired incidents, dispatches or poison.");
        var physical = new List<BookmarkStateObservation>();
        foreach (var wait in await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(workflowId))
            physical.Add(await bookmarks.ObserveAsync(workflowId, wait.BookmarkId) ?? throw new InvalidDataException("Actual compensation wait disappeared."));
        Require(physical.Count == (stage == "Finished" ? 0 : 1), "Actual compensation physical wait count differs.");
        foreach (var wait in physical)
        {
            var target = states.Single(s => s.Execution.ExecutableNodeId == expectedWait);
            Require(wait.Scope == new PersistenceScope(scopeName) && wait.Generation > 0 && wait.State.ExecutableNodeId == expectedWait && wait.State.ActivityExecutionId == target.InvocationId &&
                wait.State.WorkflowExecutionId == workflowId && wait.State.Metadata.GetValueOrDefault("runtime.correlationId") == workflowId,
                "Actual compensation wait changed invocation, scope, generation or correlation.");
        }
        var sources = new List<RuntimePostCommitOutboxItem>();
        foreach (var row in await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync())
        {
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName && row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)), "Physical compensation source identity differs.");
            var source = await outbox.FindAsync(identity) ?? throw new InvalidDataException("Checked compensation source disappeared.");
            Require(source.Status == RuntimePostCommitOutboxStatus.Delivered, "Actual compensation source remains owed."); sources.Add(source);
        }
        return new { Workflow = workflow, Activities = states.OrderBy(s => s.InvocationId, StringComparer.Ordinal), Incidents = faults, Dispatches = links,
            PhysicalBookmarks = physical.OrderBy(w => w.State.BookmarkId, StringComparer.Ordinal), Sources = sources.OrderBy(s => s.OutboxItemId, StringComparer.Ordinal),
            RetainedSemanticVersion = root.PrivateState?.StateVersion, ExactHandlerAndReplayOrderVerified = true };
    }
}
