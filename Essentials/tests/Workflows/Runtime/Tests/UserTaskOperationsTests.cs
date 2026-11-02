using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.UserTasks;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class UserTaskOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_claims_have_one_winner_and_one_atomic_history_change()
    {
        var store = Store(); await store.TryWriteAsync(Task(), null);
        var racers = new[] { Operations(store, Actor("alice")), Operations(store, Actor("bob")) };
        var results = await System.Threading.Tasks.Task.WhenAll(racers.Select(async operations =>
        {
            try { return (await operations.ClaimAsync("task", new(1, Guid.NewGuid().ToString("N")))).ClaimantId; }
            catch (UserTaskException) { return null; }
        }));
        Assert.Single(results.Where(x => x is not null));
        var task = await store.FindAsync("tenant", "task");
        Assert.Equal(UserTaskStatus.Claimed, task!.Status);
        Assert.Equal(2, task.History.Count);
        Assert.Equal(2, task.Revision);
        Assert.Equal(results.Single(x => x is not null), task.ClaimantId);
    }

    [Fact]
    public async Task Accepted_response_is_immutable_replayable_and_pending_until_runtime_proof()
    {
        var store = Store(); await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null }, null);
        var operations = Operations(store, Actor("alice"));
        var request = new UserTaskCompletion(1, "request-1", "Approve", Json("{}"));
        var pending = await operations.CompleteAsync("task", request);
        Assert.Equal(UserTaskStatus.CompletionPending, pending.Status);
        Assert.Equal("alice", pending.Decision!.ActorId);
        var replay = await operations.CompleteAsync("task", request);
        Assert.Equal(pending.Decision.Response.CompletionId, replay.Decision!.Response.CompletionId);
        Assert.Equal(pending.History.Count, replay.History.Count);
        await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", request with { Action = "Reject" }).AsTask());
        await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", request with { RequestId = "request-2", Revision = 2 }).AsTask());
        Assert.Equal(pending.Decision.Response.CompletionId, (await store.FindAsync("tenant", "task"))!.Decision!.Response.CompletionId);
    }

    [Fact]
    public async Task Group_completion_requires_claim_and_current_eligibility()
    {
        var store = Store(); await store.TryWriteAsync(Task(), null);
        var operations = Operations(store, Actor("alice"));
        await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", new(1, "complete", "Approve", Json("{}"))).AsTask());
        await operations.ClaimAsync("task", new(1, "claim"));
        var revoked = Operations(store, Actor("alice") with { GroupIds = new HashSet<string>() });
        Assert.Equal("missing", (await Assert.ThrowsAsync<UserTaskException>(() => revoked.GetAsync("task").AsTask())).Code);
        await Assert.ThrowsAsync<UserTaskException>(() => revoked.CompleteAsync("task", new(2, "complete", "Approve", Json("{}"))).AsTask());
        Assert.Equal(UserTaskStatus.Claimed, (await store.FindAsync("tenant", "task"))!.Status);
    }

    [Fact]
    public async Task Reassignment_invalidates_old_claim_and_terminal_tasks_reject_mutations()
    {
        var store = Store(); await store.TryWriteAsync(Task(), null);
        await Operations(store, Actor("alice")).ClaimAsync("task", new(1, "claim"));
        var supervisor = Operations(store, Actor("supervisor") with { CanManage = true });
        var reassigned = await supervisor.ReassignAsync("task", new(2, "reassign", "bob", null));
        Assert.Null(reassigned.ClaimantId);
        await Assert.ThrowsAsync<UserTaskException>(() => Operations(store, Actor("alice")).GetAsync("task").AsTask());
        await store.TryWriteAsync(reassigned with { Status = UserTaskStatus.Cancelled, Revision = 4 }, 3);
        await Assert.ThrowsAsync<UserTaskException>(() => Operations(store, Actor("bob")).CompleteAsync("task", new(4, "complete", "Approve", Json("{}"))).AsTask());
        await Assert.ThrowsAsync<UserTaskException>(() => supervisor.ReassignAsync("task", new(4, "again", "alice", null)).AsTask());
    }

    [Fact]
    public async Task Paging_filters_before_limit_and_never_exposes_other_tenants_or_claimants()
    {
        var store = Store();
        foreach (var task in new[] { Task() with { Id = "a", TenantId = "other" }, Task() with { Id = "b", ClaimantId = "bob", Status = UserTaskStatus.Claimed }, Task() with { Id = "c" }, Task() with { Id = "d" } })
            await store.TryWriteAsync(task, null);
        var operations = Operations(store, Actor("alice"));
        var first = await operations.ListAsync("queue", 1);
        Assert.Equal("c", Assert.Single(first.Items).Id);
        var second = await operations.ListAsync("queue", 1, first.Next);
        Assert.Equal("d", Assert.Single(second.Items).Id);
        Assert.Null(second.Next);
        await Assert.ThrowsAsync<UserTaskException>(() => operations.ListAsync("all").AsTask());
    }

    [Fact]
    public void Basic_forms_enforce_types_choices_casing_required_waiver_and_read_only_values()
    {
        var forms = new BasicUserTaskForms();
        var form = new UserTaskForm("purchase", "1", "Purchase", forms.Provider,
            [new("Amount", "Amount", "number", true, Minimum: 1, Maximum: 100), new("Note", "Note", "text", true, MaxLength: 5),
             new("Account", "Account", "text", ReadOnly: true), new("Decision", "Decision", "choice", Choices: ["A", "B"]), new("Date", "Date", "date")]);
        var prefill = Json("{\"Account\":\"A1\"}");
        Assert.Empty(forms.ValidateResponse(form, prefill, Json("{\"Amount\":10,\"Note\":\"yes\",\"Account\":\"A1\",\"Decision\":\"A\",\"Date\":\"2026-11-02\"}"), true));
        var invalid = forms.ValidateResponse(form, prefill, Json("{\"amount\":10,\"Amount\":\"10\",\"Note\":\"too long\",\"Account\":\"other\",\"Decision\":\"a\",\"Date\":\"today\"}"), true);
        Assert.Equal(6, invalid.Count);
        Assert.Empty(forms.ValidateResponse(form, prefill, prefill, false));
        Assert.Contains("Amount", forms.ValidateResponse(form, prefill, prefill, true).Keys);
        Assert.Contains("Account", forms.ValidateResponse(form, prefill, Json("{}"), false).Keys);
    }

    [Fact]
    public async Task Failed_form_validation_changes_neither_journal_nor_history()
    {
        var store = Store(); var forms = new BasicUserTaskForms();
        await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null, Form = new("f", "1", "Form", forms.Provider, [new("Reason", "Reason", "text", true)]) }, null);
        var operations = Operations(store, Actor("alice"));
        var refusal = await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", new(1, "submit", "Approve", Json("{}"))).AsTask());
        Assert.Contains("Reason", refusal.Errors!.Keys);
        var unchanged = await store.FindAsync("tenant", "task");
        Assert.Equal(1, unchanged!.Revision); Assert.Single(unchanged.History); Assert.Null(unchanged.Decision);
    }

    [Fact]
    public async Task Conditional_journal_writes_and_form_versions_are_immutable_snapshots()
    {
        var store = Store(); var original = Task();
        Assert.True(await store.TryWriteAsync(original, null));
        Assert.False(await store.TryWriteAsync(original, null));
        var a = original with { Revision = 2, Title = "A" }; var b = original with { Revision = 2, Title = "B" };
        Assert.True(await store.TryWriteAsync(a, 1)); Assert.False(await store.TryWriteAsync(b, 1));
        var fields = new List<UserTaskField> { new("A", "A", "text") };
        var version = new UserTaskFormVersion("tenant", new("f", "1", "F", UserTaskProtocol.BasicForms, fields), Now, "author");
        Assert.True(await store.TryCreateFormAsync(version)); fields.Clear();
        Assert.Single((await store.FindFormAsync("tenant", "f", "1"))!.Form.Fields);
        Assert.False(await store.TryCreateFormAsync(version));
    }

    private static MemoryUserTaskStore Store() => new(new(), new Accessor());
    private static UserTaskActor Actor(string user) => new("tenant", user, new HashSet<string> { "finance" }, true, true, false, false);
    private static UserTaskOperations Operations(IUserTaskStore store, UserTaskActor actor) => new(store, new ActorAccess(actor), [new BasicUserTaskForms()], TimeProvider.System, new Runtime(), new UserTaskCursorCodec(TimeProvider.System), new Accessor());
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static UserTaskRecord Task() => new("task", "tenant", "execution", "invocation", "node", "operation", "Purchase", null, null, "finance", null,
        ["Approve", "Reject"], [], null, Json("{}"), UserTaskStatus.Open, 1, Now, Now, [new("created", "workflow", Now)]);
    private sealed class Accessor : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class ActorAccess(UserTaskActor actor) : IUserTaskAccess
    {
        public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(actor);
        public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class Runtime : IUserTaskRuntime
    {
        public ValueTask<UserTaskRecord> StageAsync(UserTaskCreation request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<UserTaskResponse> ReadAcceptedAsync(string tenantId, string taskId, string completionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
