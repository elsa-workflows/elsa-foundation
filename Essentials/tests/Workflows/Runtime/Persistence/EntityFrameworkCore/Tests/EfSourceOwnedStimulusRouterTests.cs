using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Normal EF aggregate/router/dispatcher composition across fresh scopes; mailbox is recording, not host execution.</summary>
public sealed class EfSourceOwnedStimulusRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 5, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");
    private static readonly PersistenceScope Scope = new("tenant");

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Actual_EF_router_reopens_recorded_full_or_empty_cohort_without_new_selection(bool coalescing, bool empty)
    {
        await using var keeper = new SqliteConnection($"Data Source=file:saved-router-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(); var actors = new Actors(); var clock = new FakeTimeProvider(Now);
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock); services.AddSingleton<IWorkflowExecutionActorProvider>(actors);
        services.AddSingleton(new PersistenceModeOptions()); services.AddSingleton<IStimulusDeliveryIntentAdapter, Adapter>();
        services.AddWorkflowRuntime(); services.AddPersistenceCore("tenant");
        services.AddRuntimeEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = keeper.ConnectionString,
            RecoveryContinuationSigningKey = new string('k', 32), HierarchyCursorSigningKey = new string('h', 32) });
        services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
        if (coalescing) services.AddPersistenceModes();
        services.AddScoped<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>(); services.AddWorkflowStimulusRouter();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        PostCommitDeliveryContext source; StimulusDeliverySnapshot winner;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider; var context = sp.GetRequiredService<RuntimeDbContext>(); await context.Database.EnsureCreatedAsync();
            await sp.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Material());
            await sp.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution("publisher"));
            await sp.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new("publication", Pin.ArtifactId, "definition", "source",
                "2", Pin.DefinitionId, "published-version", "published-label", Now, Now, WorkflowExecutableReferenceScope.Published, TenantId: "tenant"));
            if (!empty) { await Bind(sp, "binding"); await Wait(sp, "original", "wait"); }
            var store = sp.GetRequiredService<EfRuntimePostCommitOutboxStore>();
            var intent = new RuntimePostCommitIntent("send", "publisher", "test.saved-event", Now, "activity", "original-key", JsonSerializer.SerializeToElement(new { value = "immutable" }));
            await store.SavePendingAsync(new("source-commit:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now,
                null, 0, null, null, null, null, null, 0, null, 1, Scope, null));
            source = new(Assert.Single(await store.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1))), Scope);
            var result = await sp.GetRequiredService<IStimulusRouter>().RouteSourceAsync(source);
            Assert.Equal(empty ? 0 : 1, result.Starts.Count); Assert.Equal(empty ? 0 : 1, result.Resumes.Count);
            winner = (await store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot!;
            Assert.Equal(1, winner.RetentionAcceptanceVersion);
        }
        Assert.Equal(empty ? 0 : 2, actors.Accepted.Count);
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider; var store = sp.GetRequiredService<EfRuntimePostCommitOutboxStore>();
            await sp.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync("publication", Now);
            await sp.GetRequiredService<IWorkflowTriggerBindingStore>().DeleteByArtifactAsync(Pin.ArtifactId);
            await Bind(sp, "late-binding"); await Wait(sp, "late", "late-wait");
            var result = await sp.GetRequiredService<IStimulusRouter>().RouteSourceAsync(source);
            Assert.Equal(empty ? 0 : 1, result.Starts.Count); Assert.Equal(empty ? 0 : 1, result.Resumes.Count);
            Assert.Equal(empty ? 0 : 2, actors.Accepted.Count);
            Assert.True(winner.IsEquivalentTo((await store.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot));
            Assert.NotNull(await sp.GetRequiredService<IBookmarkStateStore>().FindAsync("late", "late-wait"));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var sourceRow = await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
            Assert.Equal("2.0.0", sourceRow.SchemaVersion); Assert.Equal(3, sourceRow.Revision);
        }
    }

    private static WorkflowExecutable Material() => new(Pin, new ExecutableNode("node", "authored", "test/activity", "1.0.0",
        new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume"] = new("resume", "node", "test", new Dictionary<string, string>()) },
        Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    private static WorkflowExecutionState Execution(string id) => new(id, Pin, WorkflowExecutionStatus.Suspended, null,
        Now, Now, Now, null, null, null, "tenant", new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") };
    private static async Task Bind(IServiceProvider sp, string id) => await sp.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new(id,
        Pin.ArtifactId, Pin.DefinitionId, Pin.ArtifactVersion, Pin.ArtifactHash, "node", "event", "named", null, new Dictionary<string, string>(), Now));
    private static async Task Wait(IServiceProvider sp, string executionId, string id)
    {
        await sp.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Execution(executionId));
        await sp.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(new(new(id + ":activity", executionId, "node", "authored", "test/activity", "1.0.0"),
            ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
            ActivitySchedulingProvenance.From(executionId, null, null, null, null, null, null, "fixture"), null, [id], [], 0, 0, new Dictionary<string, string>()));
        await sp.GetRequiredService<IBookmarkStateStore>().SaveAsync(new(id, executionId, id + ":activity", "node", "resume", "event", "named", null, new Dictionary<string, string>(), Now, null));
    }
    private sealed class Adapter : IStimulusDeliveryIntentAdapter
    {
        public string IntentKind => "test.saved-event";
        public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent) => new("event", "named", intent.Payload,
            idempotencyKey: intent.IdempotencyKey, requestedBy: "test.saved-event") { ResumeAllMatchingBookmarks = true };
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Actors : IWorkflowExecutionActorProvider
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public List<WorkflowExecutionCommandEnvelope> Accepted { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<IWorkflowExecutionActor>(new Actor(this, request.WorkflowExecutionId));
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        private sealed class Actor(Actors owner, string executionId) : IWorkflowExecutionActor
        {
            public WorkflowExecutionActorDescriptor Descriptor { get; } = new(executionId, "recording:" + executionId, "recording-component-boundary", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, Now);
            public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
            {
                var first = owner.seen.Add(envelope.IdempotencyKey); if (first) owner.Accepted.Add(envelope);
                return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(envelope.EnvelopeId, executionId,
                    first ? WorkflowExecutionCommandDispatchStatus.Accepted : WorkflowExecutionCommandDispatchStatus.Duplicate, Now));
            }
        }
    }
}
