using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class HttpSemanticReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("semantic-http"), "tenant");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_resources_cannot_reserve_equivalent_routes_in_either_creation_order(bool reverse)
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        var first = reverse ? "orders/{orderId}" : "orders/{id}"; var second = reverse ? "orders/{id}" : "orders/{orderId}";
        await Put(services, "first", first);
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(() => Put(services, "second", second));
        Assert.Equal("route-conflict", failure.Code);
        Assert.Single((await services.GetRequiredService<IStandaloneTriggerStore>().ListAsync(Scope, new())).Items);
        Assert.Null(await services.GetRequiredService<IStandaloneTriggerStore>().FindMutationAsync(Scope, "operator", "second:1"));
        Assert.Single((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_publication_and_resource_protect_the_same_semantic_namespace_in_both_orders(bool publicationFirst)
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        if (publicationFirst)
        {
            Assert.True((await Publish(services, "definition", "first", "orders/{id}")).Succeeded);
            Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => Put(services, "resource", "orders/{orderId}"))).Code);
            Assert.Null(await services.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "resource"));
        }
        else
        {
            await Put(services, "resource", "orders/{orderId}");
            var refused = await Publish(services, "definition", "first", "orders/{id}");
            Assert.False(refused.Succeeded); Assert.Contains("equivalent endpoint", refused.Diagnostic ?? "");
            Assert.Null((await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync("definition", "Published"))?.ActiveActivationId);
        }
        Assert.Single((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
    }

    [Fact]
    public async Task Native_legacy_binding_without_reservation_or_explicit_cardinality_still_blocks_equivalent_ownership()
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        var metadata = Metadata("orders/{legacy}", null); // Legacy missing method is a wildcard; default cardinality is FanOut.
        await services.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new("legacy", "artifact", "definition", "1", "hash",
            "root", HttpEndpointRouting.StimulusType, Identity("orders/{legacy}", "legacy"), null, metadata, Now));
        Assert.Empty((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
        Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => Put(services, "resource", "orders/{id}"))).Code);
        Assert.Null(await services.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "resource"));
    }

    [Fact]
    public async Task Native_same_owner_can_rename_parameters_while_different_methods_keep_distinct_owners()
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        await Put(services, "resource", "orders/{id}");
        await Put(services, "resource", "orders/{orderId}", revision: 2);
        await Put(services, "reader", "orders/{name}", "GET");
        var reservations = (await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items;
        Assert.Equal(2, reservations.Count);
        Assert.DoesNotContain(reservations, item => item.Metadata[HttpEndpointRouting.TemplateMetadataKey] == "orders/{id}");
        Assert.Contains(reservations, item => item.OwnerId == "resource" && item.OwnerRevision == 2 &&
            item.Metadata[HttpEndpointRouting.TemplateMetadataKey] == "orders/{orderId}");
    }

    [Fact]
    public async Task Native_concurrent_equivalent_resource_commits_have_one_winner_and_one_receipt()
    {
        using var fixture = new Fixture(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> Attempt(string id, string route) => Task.Run(async () =>
        {
            using var operation = fixture.Open(); await start.Task;
            try { await Put(operation.ServiceProvider, id, route); return true; }
            catch (TriggerOperationException failure) when (failure.Code == "route-conflict") { return false; }
        });
        var first = Attempt("first", "orders/{id}"); var second = Attempt("second", "orders/{orderId}"); start.SetResult();
        var outcomes = await Task.WhenAll(first, second); Assert.Single(outcomes, item => item);
        using var verify = fixture.Open(); var store = verify.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>();
        Assert.Single((await store.ListAsync(Scope, new())).Items);
        var receipts = new[] { await store.FindMutationAsync(Scope, "operator", "first:1"), await store.FindMutationAsync(Scope, "operator", "second:1") };
        Assert.Single(receipts, item => item is not null);
    }

    private static async Task Put(IServiceProvider services, string id, string route, string method = "POST", long revision = 1)
    {
        var resource = new StandaloneTrigger(id, "Orders", "http", 1, JsonSerializer.SerializeToElement(new
            { method, path = route, sourceAccess = "Anonymous", http = new { mode = "AsyncFanOut", admissionPolicy = "RequireAll" } }),
            revision, revision, 1, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: true, tenantId: Scope.TenantId);
        var store = services.GetRequiredService<ITriggerSourceResourceCommitStore>(); Assert.True(store.SupportsActivatedResources);
        await store.CommitAsync(Scope, new(id + ":" + revision, id + ":" + revision, "operator", revision == 1 ? null : revision - 1, resource, []),
            [new(HttpEndpointRouting.StimulusType, Identity(route, method), Metadata(route, method))]);
    }

    private static async Task<WorkflowActivationResult> Publish(IServiceProvider services, string definition, string activation, string route)
    {
        var identity = new WorkflowExecutableIdentity(activation + "-artifact", definition, activation, "1", "hash");
        var root = new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), Metadata(route, "POST"));
        var executable = new WorkflowExecutable(identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), new WorkflowExecutableInputContract(1, []), [], IncidentStrategyBuiltIns.FaultReference);
        var materials = services.GetRequiredService<IWorkflowExecutableStore>(); await materials.SaveAsync(executable);
        var material = (await materials.FindMaterialAsync(identity.ArtifactId))!;
        return await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition, material,
            new("candidate", identity.ArtifactId, "definition", activation, "1", definition, activation, "1", Now, Now,
                WorkflowExecutableReferenceScope.Published) { TenantId = Scope.TenantId }, "Published", activation, WorkflowActivationSource.Publishing, 0));
    }

    private static string Identity(string route, string method) => "original:" + route + ":" + method;
    private static Dictionary<string, string> Metadata(string route, string? method)
    {
        var metadata = new Dictionary<string, string> { [HttpEndpointRouting.TemplateMetadataKey] = route };
        if (method is not null) metadata[HttpEndpointRouting.MethodMetadataKey] = method;
        return metadata;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        internal Fixture()
        {
            IServiceCollection services = new ServiceCollection(); services.AddWorkflowRuntime(); services.AddWorkflowStimulusRouter();
            // Select the owning feature's actual policy contribution; this component fixture installs no HTTP listener.
            IServiceCollection source = new ServiceCollection(); new WorkflowsRuntimeHttpFeature().ConfigureServices(source);
            services.Add(Assert.Single(source, descriptor => descriptor.ServiceType == typeof(IIngressReservationPolicy)));
            services.RemoveAll<IWorkflowTriggerIndexer>(); services.AddScoped<IWorkflowTriggerIndexer, Indexer>();
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        internal IServiceScope Open()
        {
            var scope = provider.CreateScope(); scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(Scope.Persistence)); return scope;
        }
        public void Dispose() => provider.Dispose();
    }
    private sealed class Indexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore schedules) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            var pin = executable.Identity; var metadata = executable.RootActivity.Metadata;
            var hash = Identity(metadata[HttpEndpointRouting.TemplateMetadataKey], metadata[HttpEndpointRouting.MethodMetadataKey]);
            var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, pin.ArtifactId, "root", hash), pin.ArtifactId,
                pin.DefinitionId, pin.ArtifactVersion, pin.ArtifactHash, "root", HttpEndpointRouting.StimulusType, hash, null, metadata, Now,
                activationId, slotId, TriggerCardinality.Exclusive);
            await bindings.PrepareActivationAsync(activationId, [binding], cancellationToken); await schedules.PrepareActivationAsync(activationId, [], cancellationToken);
            return [binding];
        }
    }
}
