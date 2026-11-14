using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class WorkerHttpFixtureHostEvidenceTests
{
    /// <summary>A mapped ASP.NET host using the real HTTP producer, protected reservation writer and EF ingress.
    /// TestServer is in-process HTTP evidence, not Kestrel/process-restart evidence.</summary>
    [Fact]
    public async Task Actual_standalone_HTTP_request_accepts_into_EF_and_replays_after_pause_before_ordinary_native_execution()
    {
        await using var host = await StartHostAsync(standaloneHttp: true);
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var fixture = await PrepareStandaloneHttpAsync(shell.ServiceProvider);
        using var request = StandaloneRequest("physical-request", new { text = "actual-http-input" });
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = receipt.GetProperty("deliveryId").GetString()!;
        Assert.Equal(1, receipt.GetProperty("acceptedCount").GetInt32());
        Assert.False(receipt.GetProperty("replayed").GetBoolean());

        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            Assert.IsType<EfRuntimeIngressDeliveryStore>(services.GetRequiredService<IRuntimeIngressDeliveryStore>());
            var accepted = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
            Assert.Equal(RuntimeDeliverySourceKind.StandaloneIngress, accepted.SourceKind);
            Assert.Null(accepted.Intent);
            Assert.Null(accepted.WorkflowExecutionId);
            Assert.Equal("Http", accepted.StandaloneIngress!.SourceFamily);
            var recipient = Assert.Single(accepted.SubscriptionDelivery!.Recipients);
            Assert.Equal("actual-http-input", recipient.Inputs["message"].GetString());
            Assert.NotEqual(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(accepted.SubscriptionDelivery.Progress).Phase);
            Assert.Equal([RuntimeIngressTransportState.AwaitingResponse, RuntimeIngressTransportState.WriteCompleted],
                accepted.SubscriptionDelivery.ControlFacts!.Transport.Select(item => item.State));
            var current = (await services.GetRequiredService<IStandaloneTriggerStore>().FindAsync(fixture.Scope, fixture.Trigger.Id))!;
            var trigger = current.Trigger;
            var paused = new StandaloneTrigger(trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Configuration,
                2, trigger.ConfigurationRevision, trigger.AssociationRevision, TriggerDesiredState.Paused, trigger.Origin,
                trigger.CreatedAt, DateTimeOffset.UtcNow, trigger.CreatedBy, hasActivated: true);
            await services.GetRequiredService<ITriggerManagementCommitter>().CommitAsync(fixture.Scope,
                new("pause-http", "pause-http", "operator", 1, paused, current.Subscriptions));
        }

        using var replayRequest = StandaloneRequest("physical-request", new { text = "actual-http-input" });
        using var replayResponse = await host.Client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(id, replay.GetProperty("deliveryId").GetString());
        using var newRequest = StandaloneRequest("new-request-after-pause", new { text = "refused" });
        using var pausedResponse = await host.Client.SendAsync(newRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pausedResponse.StatusCode);

        await using var resumedScope = shell.ServiceProvider.CreateAsyncScope();
        var runtime = resumedScope.ServiceProvider;
        await AwaitHttpTargetAcceptanceAsync(shell.ServiceProvider, id);
        var delivered = (await runtime.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
        var selected = Assert.Single(delivered.SubscriptionDelivery!.Recipients);
        var progress = Assert.Single(delivered.SubscriptionDelivery.Progress);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, progress.Phase);
        Assert.Equal(RuntimeTargetAcceptanceKind.InitialCheckpoint, progress.Receipt!.Kind);
        var execution = (await runtime.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(selected.StartIdentity.WorkflowExecutionId))!;
        Assert.Equal(fixture.Material, execution.PinnedExecutable);
        Assert.Equal(selected.SourceProvenance, execution.PinnedSource);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.NotNull(execution.IngressOrigin);
        Assert.Single(await runtime.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(execution.WorkflowExecutionId));
        var input = Assert.Single((await runtime.GetRequiredService<IDurableValueStateStore>().ListPageAsync(new(execution.WorkflowExecutionId))).Items,
            item => item.Metadata.GetValueOrDefault(Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.InputName) == "message");
        Assert.Equal("actual-http-input", input.InlineValue!.Value.GetString());
        Assert.Empty(await runtime.GetRequiredService<IIncidentStateStore>().ListAsync(execution.WorkflowExecutionId));
        Assert.Empty(await runtime.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(execution.WorkflowExecutionId));
    }

    [Theory]
    [InlineData("RequireAll", HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("AcceptAvailable", HttpStatusCode.Accepted, 1)]
    public async Task Actual_HTTP_policy_with_a_physically_unpublished_subscriber_never_claims_unaccepted_work(
        string policy, HttpStatusCode expectedStatus, int expectedAccepted)
    {
        await using var host = await StartHostAsync(standaloneHttp: true);
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var fixture = await PrepareStandaloneHttpAsync(shell.ServiceProvider, policy, unavailableSubscriber: true);
        using var request = StandaloneRequest("policy-request", new { text = "healthy-input" });
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedAccepted, body.GetProperty("acceptedCount").GetInt32());
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>();
        var physical = await store.ListDeliveriesAsync(fixture.Scope, new(Limit: 100));
        if (expectedAccepted == 0)
        {
            Assert.Empty(physical.Items);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
            return;
        }
        Assert.Equal("PartiallyAccepted", body.GetProperty("admission").GetString());
        var accepted = Assert.Single(physical.Items);
        Assert.Single(accepted.SubscriptionDelivery!.Recipients);
        Assert.Single(accepted.SubscriptionDelivery.Refusals);
        Assert.Equal("target-unpublished", accepted.SubscriptionDelivery.Refusals[0].ReasonCode);
        Assert.NotEqual(RuntimeRecipientPhase.CancelledBeforeDispatch, Assert.Single(accepted.SubscriptionDelivery.Progress).Phase);
    }

    private static async Task AwaitHttpTargetAcceptanceAsync(IServiceProvider shell, string sourceId)
    {
        // The normal host's recurring pump may win. Read actual monotonic evidence rather than assuming this test's
        // manual sweep owns dispatch or that an accepted source remains Pending until the assertion runs.
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
        {
            await using var operation = shell.CreateAsyncScope();
            var services = operation.ServiceProvider;
            await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100));
            var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(sourceId))!;
            if (Assert.Single(source.SubscriptionDelivery!.Progress).Phase == RuntimeRecipientPhase.TargetAccepted &&
                source.Status == RuntimePostCommitOutboxStatus.Delivered) return;
            await Task.Delay(50);
        }
        Assert.Fail("The retained HTTP recipient did not obtain its actual target acceptance receipt.");
    }

    private sealed record StandaloneHttpFixture(TriggerScope Scope, StandaloneTrigger Trigger, WorkflowExecutableIdentity Material);

    private static HttpRequestMessage StandaloneRequest(string key, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/http/physical-intake") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<StandaloneHttpFixture> PrepareStandaloneHttpAsync(IServiceProvider shell, string policy = "RequireAll", bool unavailableSubscriber = false, string mode = "AsyncFanOut", WorkflowExecutable? targetExecutable = null)
    {
        await using var scope = shell.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await PrepareStandaloneHttpSchemaAsync(services);
        // EF activation switches both projection stores, including an intentionally empty schedule projection.
        // The ordinary scheduling decorator prepares that projection; direct fixture writes cannot substitute for it.
        Assert.IsType<RecurringTriggerScheduleIndexer>(services.GetRequiredService<IWorkflowTriggerIndexer>());
        var persistence = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope();
        var triggerScope = new TriggerScope(persistence, null);
        var now = DateTimeOffset.UtcNow;
        var original = targetExecutable ?? RuntimeEventExecutableTestFixture.Create("physical-http");
        var executable = new WorkflowExecutable(original.Identity, original.RootActivity, original.ResumeTargets, original.CreatedAt,
            original.CompatibilityMetadata, new(1, [new("message", new("String"), true)]), null, original.IncidentStrategy);
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var material = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(executable.Identity.ArtifactId))!;
        var subscriptions = new List<TriggerSubscription>();
        WorkflowActivationResult? second = null;
        for (var index = 0; index < (unavailableSubscriber ? 2 : 1); index++)
        {
            var definition = "http-recipient-" + index;
            var publication = "http-publication-" + index;
            var source = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(publication), material.Identity.ArtifactId,
                "WorkflowDefinition", definition + "-version", "1", definition, definition + "-version", "1", now, now, WorkflowExecutableReferenceScope.Published);
            var published = await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition, material, source,
                "default", publication, WorkflowActivationSource.Publishing, 0));
            Assert.True(published.Succeeded, $"Native HTTP recipient activation failed: {JsonSerializer.Serialize(published)}");
            Assert.Equal(WorkflowActivationProjectionState.Active,
                await services.GetRequiredService<IWorkflowTriggerBindingStore>().FindActivationStateAsync(publication));
            Assert.Equal(WorkflowActivationProjectionState.Active,
                await services.GetRequiredService<IRecurringTriggerScheduleStore>().FindActivationStateAsync(publication));
            if (index == 1) second = published;
            var id = "http-subscription-" + index;
            subscriptions.Add(new(id, "physical-http-trigger", definition, "default", "native", 1, 1, true,
                [new("message", new TriggerSourceBinding(["payload", "text"]))], new(TriggerManagementKind.Studio),
                new("http-grant-" + index, "physical-http-trigger", id, definition, "default", "native", "http", 1, TriggerGrantStatus.Granted, "operator", now)));
        }
        var trigger = new StandaloneTrigger("physical-http-trigger", "Physical HTTP intake", "http", 1,
            JsonSerializer.SerializeToElement(new { method = "POST", path = "physical-intake", sourceAccess = "Anonymous", http = mode == "AsyncFanOut" ? (object)new { mode, admissionPolicy = policy } : new { mode } }),
            1, 1, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), now, now, "operator");
        var committer = services.GetRequiredService<ITriggerManagementCommitter>();
        await AssertStandaloneHttpSchemaReadyAsync(services);
        Assert.True(committer.CanActivateType("http", 1));
        await committer.CommitAsync(triggerScope, new("create-physical-http", "create-physical-http", "operator", null, trigger, subscriptions));
        if (second is not null)
        {
            var unpublished = await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateMaterialAsync(new(second.Slot.WorkflowDefinitionId,
                material, "default", WorkflowActivationSource.Publishing, second.Slot.Revision));
            Assert.True(unpublished.Succeeded, $"Native HTTP recipient unpublication failed: {JsonSerializer.Serialize(unpublished)}");
        }
        await services.GetRequiredService<IHttpEndpointRouteTableSynchronizer>().RefreshAsync();
        return new(triggerScope, trigger, material.Identity);
    }

    private static readonly EfSchemaChain[] StandaloneHttpSchemaChains =
    [
        StandaloneTriggersEfModule.Chain,
        RuntimeTriggerBindingEfModule.Chain,
        RuntimeArtifactEfModule.Chain,
        RuntimeOperationalStateEfModule.Chain,
        RuntimePostCommitOutboxEfModule.Chain,
        RuntimeSchedulerWorkEfModule.Chain,
        RuntimeWorkflowExecutionEfModule.Chain,
        RuntimeModelStateEfModule.Chain
    ];

    private static async Task PrepareStandaloneHttpSchemaAsync(IServiceProvider services)
    {
        // This bounded host has one active reader and no fleet. Its normal gate retains predecessor formats until
        // explicit finalization, as in SecretsPersistenceHostJourneyTests; only the standalone HTTP fixture advances.
        Assert.Null(services.GetService<IEfSchemaFleet>());
        var context = services.GetRequiredService<RuntimeDbContext>();
        var store = new EfSchemaFinalizationStore(context);
        var member = new SchemaFinalizationMember("owned-http-test-host", "test-incarnation");
        foreach (var chain in StandaloneHttpSchemaChains)
        {
            var record = await store.FindAsync(chain.Family);
            Assert.NotNull(record);
            if (record.FinalizedVersion == chain.CurrentVersion) continue;
            var intent = await store.RecordIntentAsync(chain.Family, record.Revision, chain.CurrentVersion, chain.ReadableVersions, member);
            Assert.True(intent.Applied, $"HTTP fixture schema intent for {chain.Family} lost its compare-and-set.");
            var finalized = await store.CommitIntentAsync(chain.Family, intent.Record.Revision, chain.ReadableVersions, member);
            Assert.True(finalized.Applied, $"HTTP fixture schema finalization for {chain.Family} lost its compare-and-set.");
        }
        var gate = Assert.IsType<EfSchemaModuleGate>(services.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(context.GetType()));
        await gate.RefreshAsync(context);
        await AssertStandaloneHttpSchemaReadyAsync(services);
    }

    private static async Task AssertStandaloneHttpSchemaReadyAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<RuntimeDbContext>();
        var store = new EfSchemaFinalizationStore(context);
        var gate = Assert.IsType<EfSchemaModuleGate>(services.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(context.GetType()));
        foreach (var chain in StandaloneHttpSchemaChains)
        {
            var record = await store.FindAsync(chain.Family);
            Assert.NotNull(record);
            Assert.Equal(chain.CurrentVersion, record.FinalizedVersion);
            Assert.Null(record.Intent);
            var state = gate.StateOf(chain.Family);
            Assert.NotNull(state);
            Assert.Equal(chain.CurrentVersion, state.WriteVersion);
            Assert.False(state.WritesRefused, $"HTTP fixture schema writes refused for {chain.Family}.");
        }
        var ingress = Assert.IsType<EfRuntimeIngressDeliveryStore>(services.GetRequiredService<IRuntimeIngressDeliveryStore>());
        Assert.True(ingress.SupportsAdmission, "The finalized HTTP fixture must prove actual coherent EF ingress admission before publication or resource writes.");
    }
}
