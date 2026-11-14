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

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_prepared_legacy_FanOut_HTTP_candidate_shares_semantic_ownership(bool publicationFirst)
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        if (publicationFirst)
        {
            Assert.True((await Publish(services, "legacy", "legacy", "orders/{legacy}", legacyFanOut: true)).Succeeded);
            Assert.Equal(TriggerCardinality.FanOut, Assert.Single(await services.GetRequiredService<IWorkflowTriggerBindingStore>()
                .ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType)).Cardinality);
            Assert.Single((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
                new(HttpEndpointRouting.StimulusType))).Items);
            Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => Put(services, "resource", "orders/{id}"))).Code);
        }
        else
        {
            await Put(services, "resource", "orders/{id}");
            var result = await Publish(services, "legacy", "legacy", "orders/{legacy}", legacyFanOut: true);
            Assert.False(result.Succeeded); Assert.Contains("equivalent endpoint", result.Diagnostic ?? "");
            Assert.Null((await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync("legacy", "Published"))?.ActiveActivationId);
        }
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

    [Fact]
    public async Task Native_candidate_with_two_equivalent_templates_is_refused_before_any_slot_serves()
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        var result = await Publish(services, "definition", "ambiguous", "orders/{id}", alternativeRoute: "orders/{name}");
        Assert.False(result.Succeeded); Assert.Contains("ambiguous exclusive", result.Diagnostic ?? "");
        Assert.Null((await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync("definition", "Published"))?.ActiveActivationId);
        Assert.Empty(await services.GetRequiredService<IWorkflowTriggerBindingStore>().ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType));
        Assert.Empty((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
    }

    [Fact]
    public async Task Native_same_publication_slot_can_rename_its_parameter_and_reconcile_the_old_claim()
    {
        using var fixture = new Fixture(); using var operation = fixture.Open(); var services = operation.ServiceProvider;
        var first = await Publish(services, "definition", "first", "orders/{id}"); Assert.True(first.Succeeded);
        var second = await Publish(services, "definition", "second", "orders/{name}", expectedRevision: first.Slot.Revision);
        Assert.True(second.Succeeded, second.Diagnostic);
        var claim = Assert.Single((await services.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
        Assert.Equal(first.Slot.SlotId, claim.OwnerId); Assert.Equal(second.Slot.Revision, claim.OwnerRevision);
        Assert.Equal("orders/{name}", claim.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
        Assert.Single(await services.GetRequiredService<IWorkflowTriggerBindingStore>().ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType));
    }

    [Fact]
    public async Task Native_other_scope_is_excluded_before_decoding_its_malformed_legacy_binding_metadata()
    {
        using var fixture = new Fixture(); var other = new PersistenceScope("foreign-semantic-http");
        using (var foreign = fixture.Open(other))
        {
            Assert.True((await Publish(foreign.ServiceProvider, "foreign", "foreign", "orders/{id}")).Succeeded);
            var bindings = foreign.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>();
            var binding = Assert.Single(await bindings.ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType));
            // Deliberate legacy corruption in another physical namespace must not be interpreted by this scope.
            await bindings.SaveAsync(binding with { Metadata = Metadata("orders/{", "POST") });
        }
        using var operation = fixture.Open();
        await Put(operation.ServiceProvider, "resource", "orders/{name}");
        Assert.Single((await operation.ServiceProvider.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType))).Items);
        using var verifyForeign = fixture.Open(other);
        Assert.Single((await verifyForeign.ServiceProvider.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(other,
            new(HttpEndpointRouting.StimulusType))).Items);
    }

    [Fact]
    public async Task Native_competing_publication_and_resource_leave_only_the_actual_winner()
    {
        using var fixture = new Fixture(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publication = Task.Run(async () =>
        {
            using var operation = fixture.Open(); await start.Task;
            var result = await Publish(operation.ServiceProvider, "definition", "publication", "orders/{id}");
            if (!result.Succeeded) Assert.Contains("equivalent endpoint", result.Diagnostic ?? "");
            return result.Succeeded;
        });
        var resource = Task.Run(async () =>
        {
            using var operation = fixture.Open(); await start.Task;
            try { await Put(operation.ServiceProvider, "resource", "orders/{name}"); return true; }
            catch (TriggerOperationException error) when (error.Code == "route-conflict") { return false; }
        });
        start.SetResult(); Assert.Single(await Task.WhenAll(publication, resource), accepted => accepted);
        using var verify = fixture.Open();
        var reservations = await verify.ServiceProvider.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope.Persistence,
            new(HttpEndpointRouting.StimulusType));
        Assert.Single(reservations.Items);
        var serving = (await verify.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>().FindAsync("definition", "Published"))?.ActiveActivationId is not null;
        var stored = await verify.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "resource");
        Assert.NotEqual(serving, stored is not null);
    }

    private static async Task Put(IServiceProvider services, string id, string route, string method = "POST", long revision = 1)
    {
        var resource = new StandaloneTrigger(id, "Orders", "http", 1, JsonSerializer.SerializeToElement(new
            { method, path = route, sourceAccess = "Anonymous", http = new { mode = "AsyncFanOut", admissionPolicy = "RequireAll" } }),
            revision, revision, 1, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: true, tenantId: Scope.TenantId);
        var store = services.GetRequiredService<ITriggerSourceResourceCommitStore>(); Assert.True(store.SupportsActivatedResources);
        await store.CommitAsync(Scope, new(id + ":" + revision, id + ":" + revision, "operator", revision == 1 ? null : revision - 1, resource, []) { ObservedTargets = [] },
            [new(HttpEndpointRouting.StimulusType, Identity(route, method), Metadata(route, method))]);
    }

    private static async Task<WorkflowActivationResult> Publish(IServiceProvider services, string definition, string activation, string route, string? alternativeRoute = null, long expectedRevision = 0, bool legacyFanOut = false)
    {
        var identity = new WorkflowExecutableIdentity(activation + "-artifact", definition, activation, "1", "hash");
        var metadata = Metadata(route, "POST"); if (legacyFanOut) metadata["test:legacyFanOut"] = "true"; if (alternativeRoute is not null) metadata["test:alternateRoute"] = alternativeRoute;
        var root = new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), metadata);
        var executable = new WorkflowExecutable(identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), new WorkflowExecutableInputContract(1, []), [], IncidentStrategyBuiltIns.FaultReference);
        var materials = services.GetRequiredService<IWorkflowExecutableStore>(); await materials.SaveAsync(executable);
        var material = (await materials.FindMaterialAsync(identity.ArtifactId))!;
        return await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition, material,
            new("candidate", identity.ArtifactId, "definition", activation, "1", definition, activation, "1", Now, Now,
                WorkflowExecutableReferenceScope.Published) { TenantId = Scope.TenantId }, "Published", activation, WorkflowActivationSource.Publishing, expectedRevision));
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
        internal IServiceScope Open(PersistenceScope? partition = null)
        {
            var scope = provider.CreateScope(); scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(partition ?? Scope.Persistence)); return scope;
        }
        public void Dispose() => provider.Dispose();
    }
    private sealed class Indexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore? schedules = null) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            var pin = executable.Identity; var metadata = executable.RootActivity.Metadata;
            var routes = new List<string> { metadata[HttpEndpointRouting.TemplateMetadataKey] };
            if (metadata.TryGetValue("test:alternateRoute", out var alternate)) routes.Add(alternate);
            var prepared = routes.Select(route =>
            {
                var hash = Identity(route, metadata[HttpEndpointRouting.MethodMetadataKey]);
                return new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, pin.ArtifactId, "root", hash), pin.ArtifactId,
                    pin.DefinitionId, pin.ArtifactVersion, pin.ArtifactHash, "root", HttpEndpointRouting.StimulusType, hash, null,
                    Metadata(route, metadata[HttpEndpointRouting.MethodMetadataKey]), Now, activationId, slotId,
                    metadata.ContainsKey("test:legacyFanOut") ? TriggerCardinality.FanOut : TriggerCardinality.Exclusive);
            }).ToArray();
            await bindings.PrepareActivationAsync(activationId, prepared, cancellationToken);
            if (schedules is not null) await schedules.PrepareActivationAsync(activationId, [], cancellationToken);
            return prepared;
        }
    }
}
