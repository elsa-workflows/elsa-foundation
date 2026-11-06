using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
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
            Assert.Equal(RuntimeRecipientPhase.Pending, Assert.Single(accepted.SubscriptionDelivery.Progress).Phase);
            Assert.Null(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(recipient.StartIdentity.WorkflowExecutionId));
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
        Assert.Equal(1, (await runtime.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100))).DeliveredCount);
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
        Assert.Equal(RuntimeRecipientPhase.Pending, Assert.Single(accepted.SubscriptionDelivery.Progress).Phase);
    }

    private sealed record StandaloneHttpFixture(TriggerScope Scope, StandaloneTrigger Trigger, WorkflowExecutableIdentity Material);

    private static HttpRequestMessage StandaloneRequest(string key, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/http/physical-intake") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<StandaloneHttpFixture> PrepareStandaloneHttpAsync(IServiceProvider shell, string policy = "RequireAll", bool unavailableSubscriber = false)
    {
        await using var scope = shell.CreateAsyncScope();
        var services = scope.ServiceProvider;
        // EF activation switches both projection stores, including an intentionally empty schedule projection.
        // The ordinary scheduling decorator prepares that projection; direct fixture writes cannot substitute for it.
        Assert.IsType<RecurringTriggerScheduleIndexer>(services.GetRequiredService<IWorkflowTriggerIndexer>());
        var persistence = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope();
        var triggerScope = new TriggerScope(persistence, null);
        var now = DateTimeOffset.UtcNow;
        var original = RuntimeEventExecutableTestFixture.Create("physical-http");
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
            JsonSerializer.SerializeToElement(new { method = "POST", path = "physical-intake", sourceAccess = "Anonymous", http = new { mode = "AsyncFanOut", admissionPolicy = policy } }),
            1, 1, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), now, now, "operator");
        var committer = services.GetRequiredService<ITriggerManagementCommitter>();
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
}
