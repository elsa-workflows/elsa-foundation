using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.UserTasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class UserTaskOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("title", "task title")]
    [InlineData("missing-assignment", "Select a task assignee or a group")]
    [InlineData("both-assignments", "clear the other assignment")]
    [InlineData("actions", "unique task actions")]
    [InlineData("optional-actions", "Optional form actions")]
    [InlineData("prefill", "Prefill must be a JSON object")]
    [InlineData("version", "Select a published version")]
    public void Creation_reports_the_configuration_field_to_correct(string invalid, string message)
    {
        var request = new UserTaskCreation("run", "invocation", "node", "operation", "Review", null, "alice", null,
            ["Approve", "Reject"], [], null, Json("{}"));
        UserTaskRules.ValidateCreation(request);
        request = invalid switch
        {
            "title" => request with { Title = "" },
            "missing-assignment" => request with { AssigneeId = null },
            "both-assignments" => request with { GroupId = "reviewers" },
            "actions" => request with { Actions = ["Approve", "Approve"] },
            "optional-actions" => request with { OptionalFormActions = ["Unknown"] },
            "prefill" => request with { Prefill = Json("\"{}\"") },
            "version" => request with { FormId = "purchase" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var failure = Assert.Throws<UserTaskException>(() => UserTaskRules.ValidateCreation(request));
        Assert.Equal("validation", failure.Code);
        Assert.Contains(message, failure.Message);
    }

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
    public async Task Blank_group_assignment_is_visible_in_mine_and_not_in_queue()
    {
        var store = Store();
        await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = "" }, null);
        var operations = Operations(store, Actor("alice"));

        Assert.Equal("task", Assert.Single((await operations.ListAsync("mine")).Items).Id);
        Assert.Empty((await operations.ListAsync("queue")).Items);
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

    [Fact]
    public async Task Form_drafts_reopen_publish_atomically_and_start_a_new_revision_after_publication()
    {
        var store = Store(); var operations = FormOperations(store, Actor("admin") with { CanReadForms = true, CanManageForms = true });
        var incomplete = new UserTaskForm("purchase", "1", "", UserTaskProtocol.BasicForms, []);
        var firstDraft = await operations.SaveFormDraftAsync("purchase", incomplete, 0);
        Assert.Equal(1, firstDraft.Revision); Assert.NotNull(firstDraft.Draft); Assert.Null(firstDraft.LatestPublished);
        Assert.Empty((await store.ListFormsAsync("tenant", 25)).Items);

        var reopened = await operations.GetFormDefinitionAsync("purchase");
        Assert.Equal(firstDraft.Draft!.Form!.Name, reopened.Draft!.Form!.Name);
        Assert.Equal(firstDraft.Draft.Revision, reopened.Draft.Revision);
        var published = await operations.PublishFormAsync("purchase", incomplete with { Name = "Purchase request" }, reopened.Revision);
        Assert.Equal("1", published.Form.Version);
        Assert.Null((await operations.GetFormDefinitionAsync("purchase")).Draft);
        Assert.Equal(2, (await operations.GetFormDefinitionAsync("purchase")).Revision);

        var secondDraft = await operations.SaveFormDraftAsync("purchase", published.Form with { Name = "Purchase request v2" }, 2);
        Assert.Equal(3, secondDraft.Revision); Assert.Equal("1", secondDraft.LatestPublished!.Form.Version);
        var secondPublication = await operations.PublishFormAsync("purchase", secondDraft.Draft!.Form!, secondDraft.Revision);
        Assert.Equal("2", secondPublication.Form.Version);
        Assert.Equal("Purchase request", (await store.FindFormAsync("tenant", "purchase", "1"))!.Form.Name);
        Assert.Equal(2, (await store.ListFormVersionsAsync("tenant", "purchase", 25)).Items.Count);
    }

    [Fact]
    public async Task Form_draft_discard_and_stale_collaborators_cannot_recreate_old_revisions()
    {
        var store = Store(); var operations = FormOperations(store, Actor("admin") with { CanReadForms = true, CanManageForms = true });
        var form = new UserTaskForm("purchase", "1", "Purchase", UserTaskProtocol.BasicForms, []);
        var saved = await operations.SaveFormDraftAsync("purchase", form, 0);
        foreach (var action in new Func<Task>[] {
            () => operations.SaveFormDraftAsync("purchase", form with { Name = "Stale" }, 0).AsTask(),
            () => operations.PublishFormAsync("purchase", form, 0).AsTask(),
            () => operations.DiscardFormDraftAsync("purchase", 0).AsTask()
        })
            Assert.Equal("conflict", (await Assert.ThrowsAsync<UserTaskException>(action)).Code);

        var discarded = await operations.DiscardFormDraftAsync("purchase", saved.Revision);
        Assert.Null(discarded.Draft); Assert.Equal(2, discarded.Revision);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<UserTaskException>(() => operations.SaveFormDraftAsync("purchase", form, 1).AsTask())).Code);
        var replacement = await operations.SaveFormDraftAsync("purchase", form, discarded.Revision);
        Assert.Equal(3, replacement.Revision);
    }

    [Fact]
    public async Task Form_deletion_is_permission_and_revision_gated_and_retains_published_versions()
    {
        var store = Store();
        var manager = FormOperations(store, Actor("admin") with { CanReadForms = true, CanManageForms = true });
        var form = new UserTaskForm("purchase", "1", "Purchase", UserTaskProtocol.BasicForms, []);
        Assert.True(await store.TryCreateFormAsync(new("tenant", form, Now, "admin")));

        var reader = FormOperations(store, Actor("reader") with { CanReadForms = true });
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<UserTaskException>(() => reader.DeleteFormAsync("purchase", 0).AsTask())).Code);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<UserTaskException>(() => manager.DeleteFormAsync("purchase", 1).AsTask())).Code);

        var deleted = await manager.DeleteFormAsync("purchase", 0);
        Assert.True(deleted.Deleted);
        Assert.Null(deleted.Draft);
        Assert.Equal(1, deleted.Revision);
        Assert.Empty((await manager.ListFormDefinitionsAsync()).Items);
        Assert.Empty((await manager.ListFormsAsync()).Items);
        Assert.Empty((await manager.ListFormVersionsAsync("purchase")).Items);
        Assert.False(await store.TryCreateFormAsync(new("tenant", form with { Version = "2" }, Now, "stale")));
        Assert.Equal("missing", (await Assert.ThrowsAsync<UserTaskException>(() => manager.GetFormDefinitionAsync("purchase").AsTask())).Code);
        var retained = (await manager.GetFormAsync("purchase", "1")).Form;
        Assert.Equal(form.Id, retained.Id); Assert.Equal(form.Version, retained.Version); Assert.Equal(form.Name, retained.Name);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<UserTaskException>(() => manager.SaveFormDraftAsync("purchase", form, 1).AsTask())).Code);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<UserTaskException>(() => manager.CreateFormAsync(form with { Version = "2" }).AsTask())).Code);
    }

    [Fact]
    public async Task Form_definitions_are_tenant_scoped_and_discarded_draft_only_forms_leave_no_list_entry()
    {
        var store = Store(); var manager = Actor("admin") with { CanReadForms = true, CanManageForms = true };
        var operations = FormOperations(store, manager);
        var saved = await operations.SaveFormDraftAsync("private-form", new("private-form", "1", "Private", UserTaskProtocol.BasicForms, []), 0);
        var otherTenant = FormOperations(store, manager with { TenantId = "other" });
        Assert.Empty((await otherTenant.ListFormDefinitionsAsync()).Items);
        Assert.Equal("missing", (await Assert.ThrowsAsync<UserTaskException>(() => otherTenant.GetFormDefinitionAsync("private-form").AsTask())).Code);

        var discarded = await operations.DiscardFormDraftAsync("private-form", saved.Revision);
        Assert.Equal(2, discarded.Revision);
        Assert.Empty((await operations.ListFormDefinitionsAsync()).Items);
        Assert.Equal("missing", (await Assert.ThrowsAsync<UserTaskException>(() => operations.GetFormDefinitionAsync("private-form").AsTask())).Code);
        Assert.Equal(2, (await store.FindFormDefinitionAsync("tenant", "private-form"))!.Revision);
    }

    [Fact]
    public async Task Drafts_can_preserve_unknown_controls_but_reject_unsafe_configuration_json()
    {
        var operations = FormOperations(Store(), Actor("admin") with { CanReadForms = true, CanManageForms = true });
        var unknown = new UserTaskForm("unknown-control", "1", "Unknown control", UserTaskProtocol.BasicForms,
            [new("rating", "Rating", "number") { ControlId = "example.rating/1", Configuration = Json("{\"maximum\":5}") }]);
        var saved = await operations.SaveFormDraftAsync(unknown.Id, unknown, 0);
        Assert.Equal(unknown.Id, saved.Draft!.Form!.Id);
        Assert.Equal("example.rating/1", saved.Draft.Form.Fields[0].ControlId);
        Assert.Equal(5, saved.Draft.Form.Fields[0].Configuration?.GetProperty("maximum").GetInt32());

        var oversized = unknown with
        {
            Id = "oversized-control",
            Fields = [unknown.Fields[0] with { Configuration = JsonSerializer.SerializeToElement(new { payload = new string('x', 17_000) }) }]
        };
        var oversizedFailure = await Assert.ThrowsAsync<UserTaskException>(() => operations.SaveFormDraftAsync(oversized.Id, oversized, 0).AsTask());
        Assert.Equal("validation", oversizedFailure.Code);

        var nested = "0";
        for (var index = 0; index < 18; index++) nested = $"{{\"value\":{nested}}}";
        var nestedForm = unknown with { Id = "deep-control", Fields = [unknown.Fields[0] with { Configuration = Json(nested) }] };
        var nestedFailure = await Assert.ThrowsAsync<UserTaskException>(() => operations.SaveFormDraftAsync(nestedForm.Id, nestedForm, 0).AsTask());
        Assert.Equal("validation", nestedFailure.Code);
    }

    [Fact]
    public async Task Publishing_after_a_legacy_nonnumeric_version_allocates_the_first_numeric_version()
    {
        var store = Store();
        var legacy = new UserTaskFormVersion("tenant", new("legacy", "published", "Legacy", UserTaskProtocol.BasicForms, []), Now, "author");
        Assert.True(await store.TryCreateFormAsync(legacy));
        var operations = FormOperations(store, Actor("admin") with { CanReadForms = true, CanManageForms = true });
        var publication = await operations.PublishFormAsync("legacy", legacy.Form with { Name = "Updated legacy form" }, 0);
        Assert.Equal("1", publication.Form.Version);
        Assert.Equal("published", (await store.FindFormAsync("tenant", "legacy", "published"))!.Form.Version);
        Assert.Equal(2, (await store.ListFormVersionsAsync("tenant", "legacy", 25)).Items.Count);
    }

    [Fact]
    public async Task Resource_denied_scan_positions_are_encrypted_and_bound_to_the_actor_and_query()
    {
        var store = Store();
        for (var index = 0; index < 6; index++) await store.TryWriteAsync(Task() with { Id = "hidden-task-identity-" + index }, null);
        var codec = new UserTaskCursorCodec(TimeProvider.System);
        UserTaskOperations For(UserTaskActor actor, bool deny) => new(store, new ActorAccess(actor, deny), [new BasicUserTaskForms()], TimeProvider.System, new Runtime(), codec, new Accessor());
        var page = await For(Actor("alice"), true).ListAsync("queue", 1);
        Assert.Empty(page.Items); Assert.NotNull(page.Next);
        Assert.DoesNotContain("hidden-task-identity", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(page.Next)));
        Assert.Equal("validation", (await Assert.ThrowsAsync<UserTaskException>(() => For(Actor("bob"), false).ListAsync("queue", 1, page.Next).AsTask())).Code);
        Assert.Equal("validation", (await Assert.ThrowsAsync<UserTaskException>(() => For(Actor("alice"), false).ListAsync("mine", 1, page.Next).AsTask())).Code);
    }

    [Fact]
    public async Task External_form_provider_validates_async_nested_response_and_preserves_its_immutable_receipt()
    {
        var store = Store(); var integration = new ExternalForms();
        var form = new UserTaskForm("external-purchase", "immutable-2026-11-03", "External purchase", integration.Provider, []);
        await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null, Form = form }, null);
        var operations = new UserTaskOperations(store, new ActorAccess(Actor("alice")), [new BasicUserTaskForms(), integration], TimeProvider.System, new Runtime(), new(TimeProvider.System), new Accessor());
        await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", new(1, "invalid-external", "Approve", Json("{\"Approval\":{\"AmountUSD\":0}}"))).AsTask());
        Assert.Equal(1, (await store.FindAsync("tenant", "task"))!.Revision);
        var request = new UserTaskCompletion(1, "external-client", "Approve", Json("{\"Approval\":{\"AmountUSD\":42,\"CaseSensitiveKey\":\"kept\"}}"));
        var accepted = await operations.CompleteAsync("task", request);
        Assert.Equal(2, integration.Validations); Assert.Equal(form.Version, integration.VersionValidated);
        Assert.Equal("kept", accepted.Decision!.Response.Data.GetProperty("Approval").GetProperty("CaseSensitiveKey").GetString());
        Assert.Equal(accepted.Decision.Response.CompletionId, (await operations.CompleteAsync("task", request)).Decision!.Response.CompletionId);
        foreach (var providers in new IUserTaskFormProvider[][] { [], [integration, new ExternalForms()] })
        {
            var missing = new UserTaskOperations(store, new ActorAccess(Actor("alice") with { CanManageForms = true }), providers, TimeProvider.System, new Runtime(), new(TimeProvider.System), new Accessor());
            Assert.Equal("unavailable", (await Assert.ThrowsAsync<UserTaskException>(() => missing.CreateFormAsync(form).AsTask())).Code);
        }
    }

    [Fact]
    public async Task Must_be_true_is_distinct_from_required_and_optional_actions_can_leave_it_unanswered()
    {
        var mustConfirm = new UserTaskForm("confirm", "1", "Confirm", UserTaskProtocol.BasicForms,
            [new("accepted", "Accept terms", "boolean", Required: true) { Presentation = new("radio"), MustBeTrue = true }]);
        var store = Store();
        await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null, Actions = ["Approve", "Skip"], OptionalFormActions = ["Skip"], Form = mustConfirm }, null);
        var operations = Operations(store, Actor("alice"));
        var refusal = await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", new(1, "declined", "Approve", Json("{\"accepted\":false}"))).AsTask());
        Assert.Equal("validation", refusal.Code);
        var skipped = await operations.CompleteAsync("task", new(1, "skip", "Skip", Json("{}")));
        Assert.Equal(UserTaskStatus.CompletionPending, skipped.Status);
        Assert.Empty(skipped.Decision!.Response.Data.EnumerateObject());

        var requiredOnly = mustConfirm with { Id = "required-only", Fields = [new("answer", "Answer", "boolean", Required: true)] };
        var falseStore = Store();
        await falseStore.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null, Form = requiredOnly }, null);
        var acceptedFalse = await Operations(falseStore, Actor("alice")).CompleteAsync("task", new(1, "explicit-no", "Approve", Json("{\"answer\":false}")));
        Assert.Equal(JsonValueKind.False, acceptedFalse.Decision!.Response.Data.GetProperty("answer").ValueKind);
    }

    [Fact]
    public void Basic_form_provider_rejects_unsupported_presentation_and_constraint_combinations()
    {
        var forms = new BasicUserTaskForms();
        var form = new UserTaskForm("f", "1", "F", forms.Provider, [new("choice", "Choice", "choice", Choices: ["A"]) { Presentation = new("checkbox") }]);
        Assert.Equal("validation", Assert.Throws<UserTaskException>(() => forms.ValidateDefinition(form)).Code);
        form = form with { Fields = [new("note", "Note", "text") { Presentation = new("dropdown") }] };
        Assert.Equal("validation", Assert.Throws<UserTaskException>(() => forms.ValidateDefinition(form)).Code);
        form = form with { Fields = [new("answer", "Answer", "boolean") { MustBeTrue = true, Presentation = new("checkbox", TrueLabel: " ") }] };
        Assert.Equal("validation", Assert.Throws<UserTaskException>(() => forms.ValidateDefinition(form)).Code);
    }

    [Fact]
    public async Task Custom_field_validator_is_invoked_for_task_completion_and_rejects_invalid_values()
    {
        var form = new UserTaskForm("rating-form", "1", "Rating", UserTaskProtocol.BasicForms,
            [new UserTaskField("rating", "Rating", "number", Required: true)
            {
                ControlId = "example.rating/1",
                Configuration = Json("{\"maximum\":5}")
            }]);
        var store = Store();
        await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null, Form = form }, null);
        var forms = new BasicUserTaskForms([new RatingFieldControlValidator()]);
        var operations = new UserTaskOperations(store, new ActorAccess(Actor("alice")), [forms], TimeProvider.System,
            new Runtime(), new UserTaskCursorCodec(TimeProvider.System), new Accessor());

        var failure = await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", new(1, "invalid-rating", "Approve", Json("{\"rating\":6}"))).AsTask());
        Assert.Equal("validation", failure.Code);
        Assert.Equal(1, (await store.FindAsync("tenant", "task"))!.Revision);

        var completed = await operations.CompleteAsync("task", new(1, "valid-rating", "Approve", Json("{\"rating\":4}")));
        Assert.Equal(UserTaskStatus.CompletionPending, completed.Status);
        Assert.Equal(4, completed.Decision!.Response.Data.GetProperty("rating").GetInt32());
    }

    [Fact]
    public void Blank_text_still_respects_maximum_length_when_required_fields_are_waived()
    {
        var forms = new BasicUserTaskForms(); var form = new UserTaskForm("f", "1", "F", forms.Provider, [new("Note", "Note", "text", true, MaxLength: 5)]);
        Assert.Contains("Note", forms.ValidateResponse(form, Json("{}"), JsonSerializer.SerializeToElement(new { Note = new string(' ', 1000) }), false).Keys);
    }

    [Fact]
    public async Task Accepted_pending_receipt_remains_readable_and_replayable_during_reconciliation_outage()
    {
        var store = Store(); await store.TryWriteAsync(Task() with { AssigneeId = "alice", GroupId = null }, null);
        var request = new UserTaskCompletion(1, "pending-outage", "Approve", Json("{}"));
        var accepted = await Operations(store, Actor("alice")).CompleteAsync("task", request);
        foreach (var error in new Exception[] { new TimeoutException(), new InvalidOperationException("provider retry", new TimeoutException()),
            new Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException("unavailable", "try later") })
        {
            var operations = new UserTaskOperations(store, new ActorAccess(Actor("alice")), [new BasicUserTaskForms()], TimeProvider.System, new Runtime(error), new(TimeProvider.System), new Accessor());
            Assert.Equal(UserTaskStatus.CompletionPending, (await operations.GetAsync("task")).Status);
            Assert.Equal(accepted.Decision!.Response.CompletionId, (await operations.CompleteAsync("task", request)).Decision!.Response.CompletionId);
            await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync("task", request with { Action = "Reject" }).AsTask());
        }
        var corrupt = new UserTaskOperations(store, new ActorAccess(Actor("alice")), [], TimeProvider.System, new Runtime(new InvalidDataException("corrupt")), new(TimeProvider.System), new Accessor());
        await Assert.ThrowsAsync<InvalidDataException>(() => corrupt.GetAsync("task").AsTask());
        var ct = new CancellationTokenSource(); ct.Cancel();
        var cancelled = new UserTaskOperations(store, new ActorAccess(Actor("alice")), [], TimeProvider.System, new Runtime(new OperationCanceledException(ct.Token)), new(TimeProvider.System), new Accessor());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.GetAsync("task", ct.Token).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inbox_cursors_cross_independent_API_replicas_with_shared_keys_and_keep_context_expiry(bool useRecoveryKey)
    {
        var time = new FakeTimeProvider(Now); var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
        UserTaskCursorCodec Replica(string selected) {
            var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IOptions<UserTaskOptions>>(Options.Create(new UserTaskOptions { CursorEncryptionKey = useRecoveryKey ? null : selected }));
            services.AddSingleton<IOptions<RuntimeRecoveryContinuationOptions>>(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = useRecoveryKey ? selected : null }));
            using var provider = services.BuildServiceProvider(); return ActivatorUtilities.CreateInstance<UserTaskCursorCodec>(provider);
        }
        var first = Replica(key); var second = Replica(key); var cursor = first.Encode("denied-row-identity", "scope/tenant/alice/queue")!;
        Assert.Equal("denied-row-identity", second.Decode(cursor, "scope/tenant/alice/queue"));
        Assert.DoesNotContain("denied-row-identity", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
        Assert.Throws<UserTaskException>(() => second.Decode(cursor, "scope/tenant/bob/queue"));
        Assert.Throws<UserTaskException>(() => Replica(Convert.ToBase64String(new byte[32])).Decode(cursor, "scope/tenant/alice/queue"));
        time.Advance(TimeSpan.FromMinutes(16)); Assert.Throws<UserTaskException>(() => second.Decode(cursor, "scope/tenant/alice/queue"));
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("AA==")]
    public void Malformed_configured_cursor_keys_refuse_instead_of_using_an_ephemeral_key(string invalid)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new UserTaskCursorCodec(TimeProvider.System, Options.Create(new UserTaskOptions { CursorEncryptionKey = invalid })));
        Assert.DoesNotContain(invalid, failure.Message);
    }

    private sealed class ExternalForms : IUserTaskFormProvider
    {
        public string Provider => "acme.forms/1"; public int Validations; public string? VersionValidated;
        public void ValidateDefinition(UserTaskForm form) { if (form.Id != "external-purchase" || string.IsNullOrWhiteSpace(form.Version)) throw new UserTaskException("validation", "An immutable external form reference is required."); }
        public async ValueTask<IReadOnlyDictionary<string, string>> ValidateResponseAsync(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields, CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); Validations++; VersionValidated = form.Version;
            return response.TryGetProperty("Approval", out var approval) && approval.TryGetProperty("AmountUSD", out var amount) && amount.TryGetDecimal(out var value) && value > 0
                ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Approval.AmountUSD"] = "External form validation refused this value." };
        }
    }

    private sealed class RatingFieldControlValidator : IUserTaskFieldControlValidator
    {
        public string Id => "example.rating/1";
        public string ValueKind => "number";
        public IReadOnlyList<string> ValidateDefinition(UserTaskField field) => field.Configuration is { } configuration &&
            configuration.ValueKind == JsonValueKind.Object && configuration.TryGetProperty("maximum", out var maximum) && maximum.TryGetInt32(out var value) && value == 5
                ? [] : ["A maximum of 5 is required."];
        public string? ValidateResponse(UserTaskField field, JsonElement value) => value.TryGetInt32(out var rating) && rating is >= 1 and <= 5
            ? null : "Rating must be between 1 and 5.";
    }

    private static MemoryUserTaskStore Store() => new(new(), new Accessor());
    private static UserTaskActor Actor(string user) => new("tenant", user, new HashSet<string> { "finance" }, true, true, false, false);
    private static UserTaskOperations Operations(IUserTaskStore store, UserTaskActor actor) => new(store, new ActorAccess(actor), [new BasicUserTaskForms()], TimeProvider.System, new Runtime(), new UserTaskCursorCodec(TimeProvider.System), new Accessor());
    private static UserTaskOperations FormOperations(IUserTaskStore store, UserTaskActor actor) => Operations(store, actor);
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static UserTaskRecord Task() => new("task", "tenant", "execution", "invocation", "node", "operation", "Purchase", null, null, "finance", null,
        ["Approve", "Reject"], [], null, Json("{}"), UserTaskStatus.Open, 1, Now, Now, [new("created", "workflow", Now)]);
    private sealed class Accessor : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class ActorAccess(UserTaskActor actor, bool denyRead = false) : IUserTaskAccess
    {
        public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(actor);
        public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) => denyRead && operation == "read" ? ValueTask.FromException(new UserTaskException("missing", "Task not found.")) : ValueTask.CompletedTask;
        public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class Runtime(Exception? failure = null) : IUserTaskRuntime
    {
        public ValueTask<UserTaskRecord> StageAsync(UserTaskCreation request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<UserTaskResponse> ReadAcceptedAsync(string tenantId, string taskId, string completionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default) => failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
    }
}
