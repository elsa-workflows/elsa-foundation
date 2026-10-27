using System.Globalization;
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

/// <summary>Checked immutable observations of actually authored opted-in call-control executions.</summary>
internal static partial class OptedCalledSnapshotReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string ConsumerKey = "elsa.bpmn";

    internal static async Task<IReadOnlyList<object>> ReadAsync(string snapshot, string scopeName, string receiptPath, string phase)
    {
        if (phase.StartsWith("escalation-", StringComparison.Ordinal))
            return await ReadEscalationsAsync(snapshot, scopeName, receiptPath, phase);
        Require(phase == "coded", "Unknown opted-in observation phase.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath));
        var receipt = document.RootElement;
        Require(receipt.GetProperty("passed").GetBoolean() && receipt.GetProperty("phase").GetString() == "CodedErrors", "Actual authored coded cases are required.");
        var cases = receipt.GetProperty("cases").EnumerateArray().ToArray();
        Require(cases.Length == 2 && cases.Select(item => item.GetProperty("authoredCode").GetString()).Order().SequenceEqual(new[] { "ORDER", "OTHER" }), "Both actual authored-code outcomes are required.");
        var cs = new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(snapshot)).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync();
        await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA query_only=ON"; await command.ExecuteNonQueryAsync(); }
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
            Require(EfRelationalIdentity.Decode(row.ScopeKey) == scopeName &&
                row.Id == EfRelationalIdentity.HashLengthFramed(scopeName, RuntimePostCommitOutboxIdentity.CreateProjectionValue(identity)), "An enumerated physical source differs from its exact scoped identity.");
            sources.Add(await outbox.FindAsync(identity) ?? throw new InvalidDataException("The checked source disappeared."));
        }
        var observed = new List<object>();
        foreach (var item in cases)
        {
            var code = item.GetProperty("authoredCode").GetString()!;
            var parentId = item.GetProperty("parentWorkflowExecutionId").GetString()!;
            var childId = item.GetProperty("childWorkflowExecutionId").GetString()!;
            var parent = await workflows.FindAsync(parentId) ?? throw new InvalidDataException("Actual caller missing.");
            var child = await workflows.FindAsync(childId) ?? throw new InvalidDataException("Actual child missing.");
            MatchPin(parent, item.GetProperty("parent").GetProperty("instance")); MatchPin(child, item.GetProperty("child").GetProperty("instance"));
            Require(parent.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced" && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) == "Balanced", "Actual coded persistence mode changed.");
            Require(child.ParentWorkflowExecutionId == parentId && parent.ParentWorkflowExecutionId is null && child.Status == WorkflowExecutionStatus.Faulted, "Actual child ancestry or status changed.");
            var parentStates = await activities.ListAllAsync(parentId); var childStates = await activities.ListAllAsync(childId);
            var parentRoot = Exact(parentStates, item.GetProperty("parent"), "parent-bpmn");
            var childRoot = Exact(childStates, item.GetProperty("child"), "child-bpmn");
            var call = Exact(parentStates, item.GetProperty("parent"), "node-call");
            var dispatch = (await dispatches.ListAsync(parentId)).Single();
            Require(dispatch.Mode == WorkflowDispatchMode.WaitForCompletion && dispatch.ParentActivityExecutionId == call.InvocationId &&
                dispatch.ChildWorkflowExecutionId == childId && dispatch.Status == WorkflowDispatchStatus.Faulted &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dispatch.ChildExecutable, child.PinnedExecutable), "Authoritative dispatch differs from the actual call pair.");
            Require(call.ParentActivityExecutionId == parentRoot.InvocationId && call.Provenance.ParentActivityExecutionId == parentRoot.InvocationId &&
                childRoot.ParentActivityExecutionId is null && child.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlDispatchId) == dispatch.DispatchId,
                "Actual admitted root/call linkage or dispatch hint differs.");
            Declares(dispatch.Metadata); Declares(call.Provenance.Metadata);
            var executable = await material.FindAsync(parent.PinnedExecutable.ArtifactId) ?? throw new InvalidDataException("Pinned actual caller missing.");
            Require(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable), "Pinned actual caller material changed.");
            Declares(executable.NodesById[parentRoot.Execution.ExecutableNodeId].Metadata);
            var control = call.ChildControl ?? throw new InvalidDataException("Actual terminal call has no typed control.");
            Require(control.DispatchId == dispatch.DispatchId && control.ParentWorkflowExecutionId == parentId && control.CallingActivityExecutionId == call.InvocationId &&
                control.ConsumerActivityExecutionId == parentRoot.InvocationId && control.Batch.ChildWorkflowExecutionId == childId && control.Batch.RootActivityExecutionId == childRoot.InvocationId &&
                control.Batch.ConsumerKey == ConsumerKey && control.Batch.ConsumerVersion == 1 && control.Batch.Ordinal == 0 && control.Batch.IsTerminal &&
                control.Batch.AuthoredFailureCode == code && control.Batch.Notifications.Count == 0 && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(control.ParentExecutable, parent.PinnedExecutable), "Actual authored terminal batch differs from its admitted boundary.");
            Require(childRoot.ProducedControl is not null && childRoot.ProducedControlSourceCommitId is null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(childRoot.ProducedControl, Json), JsonSerializer.SerializeToElement(control.Batch, Json)), "Actual root and carried control disagree.");
            var inbox = call.ChildControlInbox ?? throw new InvalidDataException("Actual completed caller has no control inbox.");
            Require(inbox.IsTerminalConsumed && inbox.NextOrdinal == 1 && inbox.Pending.Count == 0 && inbox.HasAccepted(control) && inbox.Target.MatchesAddress(control), "The exact caller did not atomically consume terminal zero.");
            Require(call.Status == ActivityExecutionStatus.Completed && call.Completion?.OutcomeKey == "Faulted" && call.Fault is null &&
                !string.IsNullOrWhiteSpace(call.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)), "The actual dispatch leaf lost its truthful ordinary completion receipt.");
            var source = sources.Single(entry => entry.OutboxItemId == control.SourceOutboxItemId);
            Require(source.OutboxItemId == RuntimePostCommitOutboxIdentity.CreateLogicalValue(control.SourceCommitId, control.SourceIntentId) &&
                source.Intent.IntentId == control.SourceIntentId && source.Intent.Kind == WorkflowDispatchLifecycle.ResumeParentIntentKind && source.Intent.WorkflowExecutionId == parentId &&
                source.Status == RuntimePostCommitOutboxStatus.Delivered && source.Intent.Payload is not null, "The exact actual terminal source is missing or unacknowledged.");
            var payload = source.Intent.Payload!.Value;
            Require(payload.GetProperty("dispatchId").GetString() == dispatch.DispatchId && payload.GetProperty("parentActivityExecutionId").GetString() == call.InvocationId &&
                payload.GetProperty("childWorkflowExecutionId").GetString() == childId, "The actual terminal result addresses a different call.");
            var result = payload.GetProperty("result");
            var carried = result.GetProperty("control").Deserialize<RuntimeChildControlTransport>(Json) ?? throw new InvalidDataException("Source result lost control.");
            Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(control, Json), JsonSerializer.SerializeToElement(carried, Json)), "Actual carried control differs from immutable source.");
            var diagnostics = result.GetProperty("diagnosticMetadata").Deserialize<Dictionary<string, string>>(Json)!;
            Require(diagnostics.GetValueOrDefault("code") == "child-workflow-faulted" && diagnostics.GetValueOrDefault("summary") == "The child workflow faulted." &&
                diagnostics.GetValueOrDefault("category") == "execution" && !JsonSerializer.Serialize(diagnostics, Json).Contains(code, StringComparison.Ordinal), "Ordinary safe diagnostics leaked authored classification or lost fault meaning.");
            var childIncidents = await incidents.ListAsync(childId); var parentIncidents = await incidents.ListAsync(parentId);
            Require(childRoot.Status == ActivityExecutionStatus.Faulted && childRoot.Fault?.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey) == "bpmn.error" &&
                childRoot.Fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == code, "The child lost its actual authored root fault.");
            var originalIncident = childIncidents.Single(incident => incident.ActivityExecutionId == childRoot.InvocationId);
            Require(originalIncident.IsBlocking && originalIncident.FailureType == "ActivityReturnedFault" && originalIncident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.FaultWorkflow,
                "The actual child's original blocking authored fault incident was absorbed or replaced.");
            Require(diagnostics.GetValueOrDefault("incidentCount") == childIncidents.Count.ToString(CultureInfo.InvariantCulture) &&
                childIncidents.All(incident => diagnostics.Any(pair => pair.Key.StartsWith("incidentId.", StringComparison.Ordinal) && pair.Value == incident.IncidentId)), "Safe diagnostics no longer identify the child's truthful incidents.");
            if (code == "ORDER")
                Require(parent.Status == WorkflowExecutionStatus.Completed && parentRoot.Status == ActivityExecutionStatus.Completed && parentIncidents.Count == 0 &&
                    parentStates.Single(state => state.Execution.ExecutableNodeId == "node-error-handler").Status == ActivityExecutionStatus.Completed, "Actual ORDER boundary failed to handle exactly once.");
            else
                Require(parent.Status == WorkflowExecutionStatus.Faulted && parentRoot.Status == ActivityExecutionStatus.Faulted &&
                    parentRoot.Fault?.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == code &&
                    !parentStates.Any(state => state.Execution.ExecutableNodeId == "node-error-handler") && parentIncidents.Any(incident => incident.IsBlocking), "Actual OTHER mismatch was caught or misclassified.");
            foreach (var id in new[] { parentId, childId }) Require((await ((IBookmarkStateStore)bookmarks).ListAllBookmarkStatesAsync(id)).Count == 0, "A terminal actual case retained physical waits.");
            observed.Add(new { Kind = "coded-error", AuthoredCode = code, Parent = parent, Child = child, ParentActivities = parentStates,
                ChildActivities = childStates, Dispatch = dispatch, ChildIncidents = childIncidents, ParentIncidents = parentIncidents,
                Source = source, AdmittedCapabilityVerified = true, ExactConsumedInboxVerified = true, SafeDiagnosticsVerified = true });
        }
        return observed;
    }

    private static void Declares(IReadOnlyDictionary<string, string> metadata) => Require(
        metadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlConsumerKey) == ConsumerKey && metadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlConsumerVersion) == "1",
        "Actual published caller/call did not opt in to the exact consumer protocol.");
    private static void MatchPin(WorkflowExecutionState state, JsonElement api) => Require(
        state.WorkflowExecutionId == api.GetProperty("workflowExecutionId").GetString() && state.PinnedExecutable.ArtifactId == api.GetProperty("artifactId").GetString() &&
        state.PinnedExecutable.ArtifactHash == api.GetProperty("artifactHash").GetString() && state.PinnedExecutable.DefinitionId == api.GetProperty("definitionId").GetString() &&
        state.PinnedExecutable.DefinitionVersionId == api.GetProperty("definitionVersionId").GetString() && state.PinnedExecutable.ArtifactVersion == api.GetProperty("artifactVersion").GetString(), "Actual API and persisted workflow pins differ.");
    private static ActivityExecutionState Exact(IReadOnlyCollection<ActivityExecutionState> states, JsonElement api, string node)
    {
        var previous = api.GetProperty("activities").EnumerateArray().Single(state => state.GetProperty("executableNodeId").GetString() == node);
        var actual = states.Single(state => state.Execution.ExecutableNodeId == node);
        Require(actual.InvocationId == previous.GetProperty("activityExecutionId").GetString() && actual.Execution.WorkflowExecutionId == previous.GetProperty("workflowExecutionId").GetString(), "Actual API and persisted invocation identities differ.");
        return actual;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
