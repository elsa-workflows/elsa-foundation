using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliverySchemaGateTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Admitted_SQLite_resources_refuse_equivalent_routes_in_both_orders(bool reverse)
    {
        await using var fixture = await SqliteHttpOwnership.Create(); using var operation = fixture.Open(); var sp = operation.ServiceProvider;
        await PutHttpResource(sp, "first", reverse ? "orders/{name}" : "orders/{id}");
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => PutHttpResource(sp, "second", reverse ? "orders/{id}" : "orders/{name}"));
        Assert.Equal("route-conflict", error.Code);
        var resources = sp.GetRequiredService<IStandaloneTriggerStore>();
        Assert.Single((await resources.ListAsync(new(Scope, Scope.Value), new())).Items);
        Assert.Null(await resources.FindMutationAsync(new(Scope, Scope.Value), "operator", "second:1"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Admitted_SQLite_publication_and_resource_share_semantic_ownership_in_both_orders(bool publicationFirst)
    {
        await using var fixture = await SqliteHttpOwnership.Create(); using var operation = fixture.Open(); var sp = operation.ServiceProvider;
        if (publicationFirst)
        {
            Assert.True((await PublishHttpMaterial(sp, "definition", "publication", "orders/{id}")).Succeeded);
            Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => PutHttpResource(sp, "resource", "orders/{name}"))).Code);
        }
        else
        {
            await PutHttpResource(sp, "resource", "orders/{name}");
            var result = await PublishHttpMaterial(sp, "definition", "publication", "orders/{id}");
            Assert.False(result.Succeeded); Assert.Contains("equivalent endpoint", result.Diagnostic ?? "");
        }
        Assert.Single((await sp.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope,
            new(HttpEndpointRouting.StimulusType))).Items);
    }

    [Fact]
    public async Task Admitted_SQLite_concurrent_resource_transactions_have_one_semantic_winner()
    {
        await using var fixture = await SqliteHttpOwnership.Create(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> Attempt(string id, string route) => Task.Run(async () =>
        {
            using var operation = fixture.Open(); var sp = operation.ServiceProvider;
            Assert.True(sp.GetRequiredService<ITriggerSourceResourceCommitStore>().SupportsActivatedResources); await start.Task;
            try { await PutHttpResource(sp, id, route); return true; }
            catch (TriggerOperationException error) when (error.Code == "route-conflict") { return false; }
        });
        var first = Attempt("first", "orders/{id}"); var second = Attempt("second", "orders/{name}"); start.SetResult();
        Assert.Single(await Task.WhenAll(first, second), winner => winner);
        using var verify = fixture.Open(); var store = verify.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>();
        Assert.Single((await store.ListAsync(new(Scope, Scope.Value), new())).Items);
        Assert.Single(new[] { await store.FindMutationAsync(new(Scope, Scope.Value), "operator", "first:1"),
            await store.FindMutationAsync(new(Scope, Scope.Value), "operator", "second:1") }, receipt => receipt is not null);
    }

    [Fact]
    public async Task Admitted_SQLite_concurrent_publication_and_resource_leave_only_the_actual_winner()
    {
        await using var fixture = await SqliteHttpOwnership.Create(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publication = Task.Run(async () =>
        {
            using var operation = fixture.Open(); await start.Task;
            var result = await PublishHttpMaterial(operation.ServiceProvider, "definition", "publication", "orders/{id}");
            if (!result.Succeeded) Assert.Contains("equivalent endpoint", result.Diagnostic ?? ""); return result.Succeeded;
        });
        var resource = Task.Run(async () =>
        {
            using var operation = fixture.Open(); await start.Task;
            try { await PutHttpResource(operation.ServiceProvider, "resource", "orders/{name}"); return true; }
            catch (TriggerOperationException error) when (error.Code == "route-conflict") { return false; }
        });
        start.SetResult(); Assert.Single(await Task.WhenAll(publication, resource), winner => winner);
        using var verify = fixture.Open(); var sp = verify.ServiceProvider;
        Assert.Single((await sp.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope, new(HttpEndpointRouting.StimulusType))).Items);
        var serving = (await sp.GetRequiredService<IWorkflowActivationAuthority>().FindAsync("definition", "Published"))?.ActiveActivationId is not null;
        Assert.NotEqual(serving, await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(new(Scope, Scope.Value), "resource") is not null);
    }

    [Fact]
    public async Task Admitted_SQLite_legacy_FanOut_wildcard_binding_without_reservation_still_blocks_the_route()
    {
        await using var fixture = await SqliteHttpOwnership.Create(); using var operation = fixture.Open(); var sp = operation.ServiceProvider;
        await sp.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new("legacy", "legacy-artifact", "legacy", "1", "hash", "root",
            HttpEndpointRouting.StimulusType, HttpEndpointStimulus.Hash("orders/{legacy}", "POST"), null, HttpMetadata("orders/{legacy}", null), Now));
        Assert.Empty((await sp.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope, new(HttpEndpointRouting.StimulusType))).Items);
        Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => PutHttpResource(sp, "resource", "orders/{id}"))).Code);
    }

    [Fact]
    public async Task Admitted_SQLite_same_owner_renames_and_distinct_methods_keep_their_exact_original_hashes()
    {
        await using var fixture = await SqliteHttpOwnership.Create(); using var operation = fixture.Open(); var sp = operation.ServiceProvider;
        await PutHttpResource(sp, "resource", "orders/{id}"); await PutHttpResource(sp, "resource", "orders/{name}", revision: 2);
        await PutHttpResource(sp, "reader", "orders/{key}", method: "GET");
        var claims = (await sp.GetRequiredService<IRuntimeIngressReservationReader>().ListInScopeAsync(Scope, new(HttpEndpointRouting.StimulusType))).Items;
        Assert.Equal(2, claims.Count);
        Assert.Contains(claims, claim => claim.OwnerId == "resource" && claim.OwnerRevision == 2 && claim.StimulusHash == HttpEndpointStimulus.Hash("orders/{name}", "POST"));
        Assert.Contains(claims, claim => claim.OwnerId == "reader" && claim.StimulusHash == HttpEndpointStimulus.Hash("orders/{key}", "GET"));
    }

    private static async Task PutHttpResource(IServiceProvider sp, string id, string route, string method = "POST", long revision = 1)
    {
        var scope = new TriggerScope(Scope, Scope.Value); var resource = new StandaloneTrigger(id, "Orders", "http", 1,
            JsonSerializer.SerializeToElement(new { method, path = route, sourceAccess = "Anonymous", http = new { mode = "AsyncFanOut", admissionPolicy = "RequireAll" } }),
            revision, revision, 1, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: true, tenantId: Scope.Value);
        var write = new TriggerStoreWrite(id + ":" + revision, id + ":" + revision, "operator", revision == 1 ? null : revision - 1, resource, []) { ObservedTargets = [] };
        var previous = await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(scope, id);
        var claims = await Assert.Single(sp.GetServices<ITriggerReservationPlanner>(), planner => planner.TypeKey == "http").PlanAsync(scope, previous, write);
        var participant = sp.GetRequiredService<ITriggerSourceResourceCommitStore>(); Assert.True(participant.SupportsActivatedResources);
        await participant.CommitAsync(scope, write, claims);
    }
    private static async Task<WorkflowActivationResult> PublishHttpMaterial(IServiceProvider sp, string definition, string activation, string route)
    {
        var pin = new WorkflowExecutableIdentity(activation + "-artifact", definition, activation, "1", "hash");
        var executable = new WorkflowExecutable(pin, new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), HttpMetadata(route, "POST")), new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), new WorkflowExecutableInputContract(1, []), [], IncidentStrategyBuiltIns.FaultReference);
        var materials = sp.GetRequiredService<IWorkflowExecutableStore>(); await materials.SaveAsync(executable);
        return await sp.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition,
            (await materials.FindMaterialAsync(pin.ArtifactId))!, new("candidate", pin.ArtifactId, "definition", activation, "1", definition, activation, "1", Now, Now,
                WorkflowExecutableReferenceScope.Published) { TenantId = Scope.Value }, "Published", activation, WorkflowActivationSource.Publishing, 0));
    }
    private static Dictionary<string, string> HttpMetadata(string route, string? method)
    {
        var metadata = new Dictionary<string, string> { [HttpEndpointRouting.TemplateMetadataKey] = HttpEndpointStimulus.NormalizeTemplate(route) };
        if (method is not null) metadata[HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant(); return metadata;
    }
    private sealed class HttpOwnershipIndexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore schedules) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable, string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            var pin = executable.Identity; var metadata = executable.RootActivity.Metadata;
            var hash = HttpEndpointStimulus.Hash(metadata[HttpEndpointRouting.TemplateMetadataKey], metadata[HttpEndpointRouting.MethodMetadataKey]);
            var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, pin.ArtifactId, "root", hash), pin.ArtifactId, pin.DefinitionId,
                pin.ArtifactVersion, pin.ArtifactHash, "root", HttpEndpointRouting.StimulusType, hash, null, metadata, Now, activationId, slotId, TriggerCardinality.Exclusive);
            await bindings.PrepareActivationAsync(activationId, [binding], cancellationToken); await schedules.PrepareActivationAsync(activationId, [], cancellationToken); return [binding];
        }
    }
    private sealed class SqliteHttpOwnership(Fixture schema, ServiceProvider provider, string path) : IAsyncDisposable
    {
        internal static async Task<SqliteHttpOwnership> Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "elsa-http-semantics-" + Guid.NewGuid().ToString("N") + ".db");
            var connection = "Data Source=" + path + ";Pooling=False;Default Timeout=10";
            var schema = await Fixture.CreateIngressReadiness(StandaloneTriggersEfModule.SchemaFamily, "1.0.0", connection); await schema.Finalize();
            IServiceCollection services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddSingleton(schema.Registry); new EventsFeature().ConfigureServices(services); services.AddWorkflowRuntime(); services.AddWorkflowStimulusRouter();
            services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = connection,
                CacheWorkflowExecutables = false, RecoveryContinuationSigningKey = new string('r', 64), HierarchyCursorSigningKey = new string('h', 64) });
            IServiceCollection source = new ServiceCollection(); new WorkflowsRuntimeHttpFeature().ConfigureServices(source);
            services.Add(Assert.Single(source, item => item.ServiceType == typeof(IIngressReservationPolicy)));
            new StandaloneHttpTriggerAuthoringFeature().ConfigureServices(services);
            services.RemoveAll<IWorkflowTriggerIndexer>(); services.AddScoped<IWorkflowTriggerIndexer, HttpOwnershipIndexer>();
            return new(schema, services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), path);
        }
        internal IServiceScope Open()
        { var operation = provider.CreateScope(); operation.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(Scope)); return operation; }
        public async ValueTask DisposeAsync() { await provider.DisposeAsync(); await schema.DisposeAsync(); File.Delete(path); }
    }
    private sealed partial class Fixture
    {
        internal Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFinalizationGates Registry => gates;
    }
}
