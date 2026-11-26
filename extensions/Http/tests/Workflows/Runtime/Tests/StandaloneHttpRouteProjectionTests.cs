using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class StandaloneHttpRouteProjectionTests
{
    [Fact]
    public async Task Paused_reserved_resource_projects_without_a_fabricated_binding_and_generations_move_fingerprint()
    {
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        var reader = new Reader(); var resolver = new HttpEndpointRoutesResolver(bindings, new GlobalBookmarkStimulusLookup(new InMemoryBookmarkStateStore()),
            NullLogger<HttpEndpointRoutesResolver>.Instance, reader, new Access());
        var empty = await resolver.ResolveRouteFingerprintAsync();
        reader.Rows = [Reservation(1, false)];
        var first = await resolver.ResolveRouteSetAsync();
        var route = Assert.Single(first.Routes);
        Assert.Equal("orders/{id}", route.Route);
        Assert.Equal("POST", Assert.Single(route.Methods));
        Assert.Equal(first.Fingerprint, await resolver.ResolveRouteFingerprintAsync());
        Assert.Empty(await bindings.ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType));
        reader.Rows = [Reservation(2, true)];
        var second = await resolver.ResolveRouteSetAsync();
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Equal(second.Fingerprint, await resolver.ResolveRouteFingerprintAsync());
        Assert.Equal("orders/{id}", Assert.Single(second.Routes).Route);
        var security = Assert.Single(second.Routes).Metadata.OfType<HttpRouteSecurityDispositionMetadata>().Single();
        Assert.Equal(HttpRouteSecurityDispositionKind.HostPolicy, security.Kind);
        reader.Rows = [];
        Assert.Empty((await resolver.ResolveRouteSetAsync()).Routes);
        Assert.Equal(empty, await resolver.ResolveRouteFingerprintAsync());
    }

    [Fact]
    public async Task Background_projection_uses_physical_scope_without_guessing_tenant_identity()
    {
        var reader = new Reader { Rows = [Reservation(1, false)] };
        var resolver = new HttpEndpointRoutesResolver(new InMemoryWorkflowTriggerBindingStore(),
            new GlobalBookmarkStimulusLookup(new InMemoryBookmarkStateStore()), NullLogger<HttpEndpointRoutesResolver>.Instance, reader, new Access());
        Assert.Single(await resolver.ResolveRoutesAsync());
        Assert.Equal("physical-partition", reader.Scope!.Value);
        Assert.Equal("separate-tenant-identity", reader.Rows[0].Scope.TenantId);
        Assert.Equal(0, reader.TenantReads);
    }

    private static RuntimeIngressReservation Reservation(long revision, bool authorize) => new("reservation", new(new("physical-partition"), "separate-tenant-identity"),
        HttpEndpointRouting.StimulusType, "hash", RuntimeIngressReservation.StandaloneTriggerOwner, "trigger", revision, revision,
        new Dictionary<string, string> { [HttpEndpointRouting.TemplateMetadataKey] = "orders/{id}",
            [HttpEndpointRouting.MethodMetadataKey] = "post", [HttpEndpointRouting.AuthorizeMetadataKey] = authorize.ToString(), ["http:triggerState"] = "Paused" });
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("physical-partition")); }
    private sealed class Reader : IRuntimeIngressReservationReader
    {
        internal IReadOnlyList<RuntimeIngressReservation> Rows = [];
        internal PersistenceScope? Scope; internal int TenantReads;
        public bool SupportsReservations => true;
        public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string stimulusType, string stimulusHash, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Background projection must not invent a tenant.");
        public ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
        { TenantReads++; throw new InvalidOperationException("Background projection must use trusted physical-scope projection."); }
        public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
        { Scope = scope; return ValueTask.FromResult(new RuntimeIngressReservationPage(Rows.Where(row => row.Scope.Persistence == scope).ToArray(), null)); }
    }
}
