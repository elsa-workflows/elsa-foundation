using System.Text.Json;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData("resource", "POST", false)] [InlineData("publication", "POST", false)]
    [InlineData("resource", "GET", true)] [InlineData("legacy", null, false)]
    public async Task HTTP_review_uses_the_registered_semantic_owner_for_distinct_original_hashes_and_legacy_wildcards(string owner, string? method, bool allowed)
    {
        IServiceCollection services = new ServiceCollection(); new WorkflowsRuntimeHttpFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(); var reader = new ReservationReader(); var bindings = new InMemoryWorkflowTriggerBindingStore();
        var fixture = HttpPreviewFixture(reader, provider.GetServices<IIngressReservationPolicy>(), bindings, [new SemanticPlanner()]);
        var metadata = RouteMetadata("orders/{id}", method);
        if (owner == "legacy")
            await bindings.SaveAsync(new("legacy-route", "artifact", "private-definition", "1", "hash", "root", HttpEndpointRouting.StimulusType,
                "old-original", null, metadata, fixture.Clock.Now)); // The default cardinality remains FanOut.
        else
            reader.Row = new("reservation", fixture.Access.Scope, HttpEndpointRouting.StimulusType, "old-original",
                owner == "resource" ? RuntimeIngressReservation.StandaloneTriggerOwner : RuntimeIngressReservation.PublicationSlotOwner,
                "private-owner", 1, 1, metadata);
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new
            { expectedRevision = 1, command = new { kind = "EnableTrigger" } }), default);
        Assert.Equal(allowed, preview.CanApply);
        if (!allowed) Assert.Contains(preview.Findings, finding => finding.Code == "route-conflict");
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(preview)); Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task Semantic_route_review_walks_every_physical_page_before_declaring_availability()
    {
        IServiceCollection services = new ServiceCollection(); new WorkflowsRuntimeHttpFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(); var reader = new PagedSemanticReader();
        var fixture = HttpPreviewFixture(reader, provider.GetServices<IIngressReservationPolicy>(), new InMemoryWorkflowTriggerBindingStore(), [new SemanticPlanner()]);
        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new
            { expectedRevision = 1, command = new { kind = "EnableTrigger" } }), default);
        Assert.False(preview.CanApply); Assert.Equal(2, reader.Reads);
        Assert.Contains(preview.Findings, finding => finding.Code == "route-conflict"); Assert.Equal(0, fixture.Store.Writes);
    }

    private sealed class PagedSemanticReader : IRuntimeIngressReservationReader
    {
        public bool SupportsReservations => true;
        internal int Reads;
        public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken ct = default)
        {
            Reads++; Assert.Null(query.StimulusHash);
            var first = query.After is null;
            var row = new RuntimeIngressReservation(first ? "first" : "second", new(scope, "tenant-a"), HttpEndpointRouting.StimulusType,
                first ? "get-original" : "post-original", RuntimeIngressReservation.StandaloneTriggerOwner, "another-resource", 1, 1,
                RouteMetadata("orders/{id}", first ? "GET" : "POST"));
            return ValueTask.FromResult(new RuntimeIngressReservationPage([row], first ? "after-first" : null));
        }
        public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string type, string hash, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static Dictionary<string, string> RouteMetadata(string route, string? method)
    {
        var values = new Dictionary<string, string> { [HttpEndpointRouting.TemplateMetadataKey] = route };
        if (method is not null) values[HttpEndpointRouting.MethodMetadataKey] = method; return values;
    }
    private sealed class SemanticPlanner : ITriggerReservationPlanner
    {
        public string TypeKey => "http";
        public ValueTask<IReadOnlyList<RuntimeIngressReservationDescriptor>> PlanAsync(TriggerScope scope, TriggerAggregate? current,
            TriggerStoreWrite proposed, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<RuntimeIngressReservationDescriptor>>(
                [new(HttpEndpointRouting.StimulusType, "new-original", RouteMetadata("orders/{orderId}", "POST"))]);
    }
}
