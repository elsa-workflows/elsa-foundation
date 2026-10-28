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
    internal static async Task<object> ReadNativeParentAsync(string snapshot, string scopeName, string apiPath, string markerPath)
    {
        using var apiDocument = JsonDocument.Parse(await File.ReadAllTextAsync(apiPath));
        var api = apiDocument.RootElement; var view = api.GetProperty("after");
        var executionId = api.GetProperty("workflowExecutionId").GetString()!;
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(); }
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var access = new ScopedAccess(new PersistenceScope(scopeName));
        var workflows = new EfWorkflowExecutionStateStore(context, access, new NoPaging());
        var activities = new EfActivityExecutionStateStore(context, access, new NoPaging());
        var workflow = await workflows.FindAsync(executionId) ?? throw new InvalidDataException("Actual native workflow missing.");
        MatchPin(workflow, view.GetProperty("instance"));
        Require(workflow.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == api.GetProperty("persistenceMode").GetString(), "Native persistence mode differs.");
        var states = await activities.ListAllAsync(executionId);
        var native = Exact(states, view, "native-parent"); var bpmn = Exact(states, view, "legacy-bpmn");
        Require(bpmn.Status == ActivityExecutionStatus.Faulted && bpmn.ParentActivityExecutionId == native.Execution.ActivityExecutionId,
            "Actual BPMN fault/parent identity differs.");
        var pending = api.GetProperty("pendingParentConsumption").GetBoolean();
        string? sourceId = null;
        if (pending)
        {
            Require(workflow.Status is WorkflowExecutionStatus.Running or WorkflowExecutionStatus.Faulted && native.Status == ActivityExecutionStatus.Running &&
                states.All(state => state.Execution.ExecutableNodeId != "after-bpmn"), "Native parent has already advanced.");
            using var markerDocument = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath)); var marker = markerDocument.RootElement;
            Require(marker.GetProperty("providerEvent").GetString() == "EF.TransactionCommitted" &&
                marker.GetProperty("phase").GetString() == "native-parent-completion-before-consumption" &&
                marker.GetProperty("workflowExecutionId").GetString() == executionId && marker.GetProperty("scope").GetString() == scopeName,
                "Actual scoped physical commit marker missing.");
            sourceId = marker.GetProperty("sourceOutboxItemId").GetString()!;
            var outbox = new EfRuntimePostCommitOutboxStore(context, access);
            var source = await outbox.FindAsync(sourceId) ?? throw new InvalidDataException("Actual pending callback source missing.");
            Require(source.Status == RuntimePostCommitOutboxStatus.Pending && source.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
                source.Intent.WorkflowExecutionId == executionId && source.Intent.Payload is not null, "Callback is no longer owed by the original checkpoint.");
            var work = source.Intent.Payload!.Value; var payload = work.GetProperty("Payload");
            Require(work.GetProperty("CommandKind").GetInt32() == (int)WorkflowExecutionCommandKind.CompleteActivity &&
                work.GetProperty("WorkflowExecutionId").GetString() == executionId &&
                work.GetProperty("WorkItemId").GetString() == marker.GetProperty("work").GetProperty("WorkItemId").GetString() &&
                payload.GetProperty("ActivityExecutionId").GetString() == native.Execution.ActivityExecutionId &&
                payload.GetProperty("CompletedChildActivityExecutionId").GetString() == bpmn.Execution.ActivityExecutionId &&
                payload.GetProperty("ExecutableNodeId").GetString() == "native-parent" &&
                payload.GetProperty("CompletionKind").GetInt32() is (int)SchedulerCompletionKind.ParentCompletionEvaluation or (int)SchedulerCompletionKind.ParentOutcomeEvaluation &&
                payload.GetProperty("PinnedExecutable").GetProperty("ArtifactId").GetString() == workflow.PinnedExecutable.ArtifactId,
                "The actual pending work does not address the exact completed BPMN child and unadvanced native parent.");
        }
        else Require(api.GetProperty("persistenceMode").GetString() == "Balanced" && workflow.Status == WorkflowExecutionStatus.Completed &&
            native.Status == ActivityExecutionStatus.Completed && Exact(states, view, "after-bpmn").Status == ActivityExecutionStatus.Completed,
            "The folded path has no actual completed native-parent projection.");
        return new { WorkflowExecutionId = executionId, PendingParentConsumption = pending, SourceOutboxItemId = sourceId,
            NativeActivityExecutionId = native.Execution.ActivityExecutionId, BpmnActivityExecutionId = bpmn.Execution.ActivityExecutionId,
            BalancedFoldedAlreadyConsumed = !pending };
    }
}
