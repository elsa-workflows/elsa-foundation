using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class UserTaskRuntimeJourneyTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Open_and_accepted_tasks_survive_restart_and_complete_exactly_one_wait(PersistenceMode mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-user-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "runtime.db"); string taskId;
        try
        {
            await using (var first = Build(database, mode, "first"))
            {
                var source = await first.PublishAsync(Executable(), "task-published");
                await first.StartPublishedAsync(source, "user-task-run", tenantId: "default", partition: new("default"));
                await first.SweepUntilQuietAsync();
                var staged = Assert.Single((await first.Services.GetRequiredService<IUserTaskStore>().RecoverAsync(10, null)).Items);
                taskId = staged.Id;
                await first.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(staged);
                var available = await first.Services.GetRequiredService<IUserTaskOperations>().GetAsync(taskId);
                Assert.Equal(UserTaskStatus.Open, available.Status);
                Assert.NotNull(available.Recipient?.RegistrationCapture);
            }
            // New provider/context generation over the same database, without re-running the activity.
            await using (var second = Build(database, mode, "second"))
            {
                var tasks = second.Services.GetRequiredService<IUserTaskOperations>();
                var task = await tasks.GetAsync(taskId);
                var accepted = await tasks.CompleteAsync(taskId, new(task.Revision, "purchase-response", "Approve", JsonSerializer.SerializeToElement(new { })));
                Assert.Equal(UserTaskStatus.CompletionPending, accepted.Status);
            }
            // Crash after durable response acceptance, before any explicit delivery/reconciliation tick.
            await using (var third = Build(database, mode, "third"))
            {
                var store = third.Services.GetRequiredService<IUserTaskStore>();
                var pending = (await store.FindAsync("default", taskId))!;
                Assert.Equal(UserTaskStatus.CompletionPending, pending.Status);
                var runtime = third.Services.GetRequiredService<IUserTaskRuntime>();
                await runtime.ReconcileAsync(pending);
                await third.SweepUntilQuietAsync();
                Assert.Equal(UserTaskStatus.CompletionPending, (await store.FindAsync("default", taskId))!.Status);
                Assert.False(await third.Services.GetRequiredService<IWorkflowExecutionStateStore>().DeleteAsync("user-task-run"));
                await runtime.ReconcileAsync(pending); // acknowledgement reads canonical committed completion, then replay is inert.
                var completed = await store.FindAsync("default", taskId);
                Assert.Equal(UserTaskStatus.Completed, completed!.Status);
                Assert.Equal("Approve", completed.Decision!.Response.Action);
                var state = await third.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync("user-task-run", completed.InvocationId);
                Assert.Equal(ActivityExecutionStatus.Completed, state!.Status);
                Assert.Equal("Approve", state.Completion!.OutcomeKey);
                Assert.Single(completed.History.Where(x => x.Operation == "accepted"));
                Assert.Single(completed.History.Where(x => x.Operation == "completed"));
                Assert.Empty((await third.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new("user-task-run"))).Items);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_bookmark_is_uncertainty_but_physical_replacement_retires_the_old_task(bool replace)
    {
        await WithOpenTask(async (host, task) =>
        {
            var bookmarks = host.Services.GetRequiredService<IBookmarkStateStore>();
            var observed = (await bookmarks.ObserveAsync(task.WorkflowExecutionId, task.Recipient!.BookmarkId))!;
            if (replace) await bookmarks.SaveAsync(observed.State);
            else Assert.True(await bookmarks.DeleteAsync(task.WorkflowExecutionId, observed.State.BookmarkId));
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(task);
            var current = (await host.Services.GetRequiredService<IUserTaskStore>().FindAsync(task.TenantId, task.Id))!;
            Assert.Equal(replace ? UserTaskStatus.Cancelled : UserTaskStatus.Open, current.Status);
            Assert.Null(current.Decision);
            if (replace) { Assert.Equal("wait-replaced", current.History.Last().Operation); Assert.NotNull(await bookmarks.FindAsync(task.WorkflowExecutionId, observed.State.BookmarkId)); }
        });
    }

    [Fact]
    public async Task A_forged_resume_preserves_the_wait_and_a_later_accepted_response_completes_once()
    {
        await WithOpenTask(async (host, task) =>
        {
            var fake = new UserTaskResponse(task.Id, "forged-completion", "Approve", JsonSerializer.SerializeToElement(new { }));
            await host.Services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(task.WorkflowExecutionId,
                UserTaskProtocol.StimulusType, task.Id, JsonSerializer.SerializeToElement(fake), "forged-request", "test", null, null, null, task.Recipient!.BookmarkId, null) { ExpectedRecipient = task.Recipient });
            await host.SweepUntilQuietAsync();
            Assert.NotNull(await host.Services.GetRequiredService<IBookmarkStateStore>().FindAsync(task.WorkflowExecutionId, task.Recipient.BookmarkId));
            var pending = await host.Services.GetRequiredService<IUserTaskOperations>().CompleteAsync(task.Id, new(task.Revision, "valid-response", "Reject", JsonSerializer.SerializeToElement(new { })));
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(pending); await host.SweepUntilQuietAsync();
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(pending);
            var result = (await host.Services.GetRequiredService<IUserTaskStore>().FindAsync(task.TenantId, task.Id))!;
            Assert.Equal(UserTaskStatus.Completed, result.Status); Assert.Equal("Reject", result.Decision!.Response.Action);
            Assert.Single(result.History, x => x.Operation == "completed");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_cancellation_retires_open_or_already_accepted_tasks(bool accepted)
    {
        await WithOpenTask(async (host, task) =>
        {
            if (accepted) task = await host.Services.GetRequiredService<IUserTaskOperations>().CompleteAsync(task.Id, new(task.Revision, "accepted-before-cancel", "Approve", JsonSerializer.SerializeToElement(new { })));
            await host.CancelAsync(task.WorkflowExecutionId, new("default"));
            await host.SweepUntilQuietAsync(); await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(task);
            var retired = (await host.Services.GetRequiredService<IUserTaskStore>().FindAsync(task.TenantId, task.Id))!;
            Assert.Equal(UserTaskStatus.Cancelled, retired.Status);
            Assert.DoesNotContain(retired.History, x => x.Operation == "completed");
            await Assert.ThrowsAsync<UserTaskException>(() => host.Services.GetRequiredService<IUserTaskOperations>().CompleteAsync(task.Id, new(retired.Revision, "late", "Approve", JsonSerializer.SerializeToElement(new { }))).AsTask());
        });
    }

    [Theory]
    [InlineData(2)] // Guard read, before consuming the physical wait.
    [InlineData(3)] // Accepted-result reread, after the durable bookmark claim.
    public async Task Wrapped_transient_journal_reads_preserve_owed_work_and_retry_to_one_completion(int failAt)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-task-outage-"); var database = Path.Combine(directory.FullName, "runtime.db");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow); var failure = new FailJournalRead();
        try
        {
            await using var host = Build(database, PersistenceMode.Durable, "outage", services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.AddScoped<RuntimeDbContext>(_ => new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                    .UseSqlite($"Data Source={database};Pooling=False").AddInterceptors(failure).Options));
            });
            var task = await OpenTask(host);
            var pending = await host.Services.GetRequiredService<IUserTaskOperations>().CompleteAsync(task.Id, new(task.Revision, "response-before-outage", "Approve", JsonSerializer.SerializeToElement(new { })));
            failure.Arm(failAt);
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(pending);
            Assert.Equal(1, failure.Failures);
            var activity = (await host.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(task.WorkflowExecutionId, task.InvocationId))!;
            Assert.Null(activity.Fault); Assert.NotEqual(ActivityExecutionStatus.Faulted, activity.Status);
            Assert.Equal(UserTaskStatus.CompletionPending, (await host.Services.GetRequiredService<IUserTaskStore>().FindAsync(task.TenantId, task.Id))!.Status);
            clock.Advance(TimeSpan.FromMinutes(2)); await host.SweepUntilQuietAsync();
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(pending); await host.SweepUntilQuietAsync();
            await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(pending);
            var result = (await host.Services.GetRequiredService<IUserTaskStore>().FindAsync(task.TenantId, task.Id))!;
            Assert.Equal(UserTaskStatus.Completed, result.Status); Assert.Single(result.History, x => x.Operation == "completed");
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task External_form_validation_and_nested_response_follow_the_same_durable_wait_contract()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-external-task-");
        try
        {
            await using var host = Build(Path.Combine(directory.FullName, "runtime.db"), PersistenceMode.Durable, "external",
                services => services.AddScoped<IUserTaskFormProvider, ExternalPurchaseFormProvider>());
            var form = new UserTaskForm("external-purchase", "immutable-1", "External purchase", "acme.forms/1", []);
            Assert.True(await host.Services.GetRequiredService<IUserTaskStore>().TryCreateFormAsync(new("default", form, DateTimeOffset.UtcNow, "author")));
            var source = await host.PublishAsync(Executable(form.Id, form.Version), "external-task-published");
            await host.StartPublishedAsync(source, "user-task-run", tenantId: "default", partition: new("default")); await host.SweepUntilQuietAsync();
            var store = host.Services.GetRequiredService<IUserTaskStore>(); var runtime = host.Services.GetRequiredService<IUserTaskRuntime>();
            var task = Assert.Single((await store.RecoverAsync(10, null)).Items); await runtime.ReconcileAsync(task);
            var operations = host.Services.GetRequiredService<IUserTaskOperations>(); task = await operations.GetAsync(task.Id);
            var invalid = await Assert.ThrowsAsync<UserTaskException>(() => operations.CompleteAsync(task.Id, new(task.Revision, "invalid-external", "Approve", JsonSerializer.SerializeToElement(new { }))).AsTask());
            Assert.Contains("Approval.AmountUSD", invalid.Errors!.Keys); Assert.NotNull(await host.Services.GetRequiredService<IBookmarkStateStore>().FindAsync(task.WorkflowExecutionId, task.Recipient!.BookmarkId));
            var data = JsonDocument.Parse("{\"Approval\":{\"AmountUSD\":42,\"CaseSensitiveKey\":\"kept\"}}").RootElement.Clone();
            var request = new UserTaskCompletion(task.Revision, "external-client", "Approve", data);
            var pending = await operations.CompleteAsync(task.Id, request); await runtime.ReconcileAsync(pending); await host.SweepUntilQuietAsync(); await runtime.ReconcileAsync(pending);
            var complete = await operations.CompleteAsync(task.Id, request); Assert.Equal(UserTaskStatus.Completed, complete.Status);
            Assert.True(JsonElement.DeepEquals(data, complete.Decision!.Response.Data)); Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(form), JsonSerializer.SerializeToElement(complete.Form)));
            var state = await host.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(task.WorkflowExecutionId, task.InvocationId);
            Assert.Equal("Approve", state!.Completion!.OutcomeKey); Assert.True(JsonElement.DeepEquals(data, state.Completion.Result.InlineValue!.Value.GetProperty("data")));
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Volatile_tasks_require_explicit_development_opt_in(bool allow)
    {
        await using var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services => {
                services.Replace(ServiceDescriptor.Scoped<IUserTaskAccess, ActorAccess>());
                services.Configure<UserTaskOptions>(options => options.AllowVolatileTasksForDevelopment = allow);
            }).Build(WorkflowExecutionHarness.Identity, "user-task-run", Enumerable.Range(1, 30).Select(x => "volatile-" + x));
        host.InitializeActivityTypes();
        var source = await host.PublishAsync(Executable(), "volatile-task");
        await host.StartPublishedAsync(source, "user-task-run", tenantId: "default", partition: new("default")); await host.SweepUntilQuietAsync();
        var tasks = (await host.Services.GetRequiredService<IUserTaskStore>().RecoverAsync(10, null)).Items;
        if (allow)
        {
            var task = Assert.Single(tasks); var runtime = host.Services.GetRequiredService<IUserTaskRuntime>();
            await runtime.ReconcileAsync(task); var operations = host.Services.GetRequiredService<IUserTaskOperations>(); task = await operations.GetAsync(task.Id);
            var waiting = await host.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(task.WorkflowExecutionId, task.InvocationId);
            Assert.Equal(ActivityExecutionStatus.Suspended, waiting!.Status);
            var pending = await operations.CompleteAsync(task.Id, new(task.Revision, "volatile-response", "Approve", JsonSerializer.SerializeToElement(new { })));
            await runtime.ReconcileAsync(pending); await host.SweepUntilQuietAsync(); await runtime.ReconcileAsync(pending);
            Assert.Equal(UserTaskStatus.Completed, (await operations.GetAsync(task.Id)).Status);
        }
        else Assert.Empty(tasks);
        var state = await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("user-task-run");
        Assert.Equal(allow ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted, state!.Status);
    }

    private sealed class ExternalPurchaseFormProvider : IUserTaskFormProvider
    {
        public string Provider => "acme.forms/1";
        public void ValidateDefinition(UserTaskForm form) { if (form.Id != "external-purchase" || form.Version != "immutable-1") throw new UserTaskException("validation", "Unknown immutable external form."); }
        public async ValueTask<IReadOnlyDictionary<string, string>> ValidateResponseAsync(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields, CancellationToken cancellationToken = default)
        {
            await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
            return !requireFields || response.TryGetProperty("Approval", out var approval) && approval.TryGetProperty("AmountUSD", out var amount) && amount.TryGetDecimal(out var number) && number > 0
                ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Approval.AmountUSD"] = "Enter a positive external amount." };
        }
    }

    private sealed class FailJournalRead : DbCommandInterceptor
    {
        private int remaining; public int Failures;
        public void Arm(int reads) => remaining = reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (remaining > 0 && command.CommandText.Contains("FROM \"UserTasks\"", StringComparison.Ordinal) && --remaining == 0)
            { Failures++; throw new InvalidOperationException("Provider read context", new TimeoutException("Injected temporary journal outage")); }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task WithOpenTask(Func<WorkflowExecutionHarness, UserTaskRecord, Task> test)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-user-task-journey-");
        try { await using var host = Build(Path.Combine(directory.FullName, "runtime.db"), PersistenceMode.Durable, "journey"); await test(host, await OpenTask(host)); }
        finally { directory.Delete(true); }
    }
    private static async Task<UserTaskRecord> OpenTask(WorkflowExecutionHarness host)
    {
        var source = await host.PublishAsync(Executable(), "task-published");
        await host.StartPublishedAsync(source, "user-task-run", tenantId: "default", partition: new("default")); await host.SweepUntilQuietAsync();
        var staged = Assert.Single((await host.Services.GetRequiredService<IUserTaskStore>().RecoverAsync(10, null)).Items);
        await host.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(staged);
        return await host.Services.GetRequiredService<IUserTaskOperations>().GetAsync(staged.Id);
    }

    private static WorkflowExecutionHarness Build(string database, PersistenceMode mode, string generation, Action<IServiceCollection>? configure = null)
    {
        var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRuntimeEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    RecoveryContinuationSigningKey = "user-task-recovery-key-at-least-32-bytes", HierarchyCursorSigningKey = "user-task-hierarchy-key-at-least-32-bytes" });
                services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                services.Replace(ServiceDescriptor.Scoped<IUserTaskAccess, ActorAccess>());
                configure?.Invoke(services);
            }).WithPersistenceMode(mode).Build(WorkflowExecutionHarness.Identity, "user-task-run", Enumerable.Range(1, 30).Select(x => $"task-{generation}-{x}"));
        foreach (var initializer in host.Services.GetServices<IShellInitializer>()) initializer.InitializeAsync().GetAwaiter().GetResult();
        host.InitializeActivityTypes();
        return host;
    }

    private static WorkflowExecutable Executable(string? formId = null, string? formVersion = null)
    {
        var original = ClrActivityContractTestBuilder.BuildContract(typeof(CreateUserTask));
        var contract = new ActivityContract(original.ActivityTypeKey, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
            original.Inputs.Values, original.Result, ["Approve", "Reject", "Unmatched"], original.Activation);
        RuntimeInputBinding Literal(string key, object value) => new(key, contract.Inputs[key].Type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(contract.Inputs[key].Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(CreateUserTask.Title)] = Literal(nameof(CreateUserTask.Title), "Purchase approval"),
            [nameof(CreateUserTask.AssigneeId)] = Literal(nameof(CreateUserTask.AssigneeId), "alice"),
            [nameof(CreateUserTask.Actions)] = Literal(nameof(CreateUserTask.Actions), new[] { "Approve", "Reject" }),
            [nameof(CreateUserTask.OptionalFormActions)] = Literal(nameof(CreateUserTask.OptionalFormActions), Array.Empty<string>()),
            [nameof(CreateUserTask.Prefill)] = Literal(nameof(CreateUserTask.Prefill), new { })
        };
        if (formId is not null) bindings[nameof(CreateUserTask.FormId)] = Literal(nameof(CreateUserTask.FormId), formId);
        if (formVersion is not null) bindings[nameof(CreateUserTask.FormVersion)] = Literal(nameof(CreateUserTask.FormVersion), formVersion);
        var node = new ExecutableNode("task-node", "authored-task", UserTaskProtocol.ActivityType, "1.0.0", contract.DescriptorKind, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings), new Dictionary<string, string>(), activityContract: contract);
        return new(WorkflowExecutionHarness.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [UserTaskProtocol.ResumeTarget] = new(UserTaskProtocol.ResumeTarget, "task-node", "ResumeAsync", new Dictionary<string, string>())
        }, WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class ActorAccess : IUserTaskAccess
    {
        public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new UserTaskActor("default", "alice", new HashSet<string>(), true, true, true, true, true));
        public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
