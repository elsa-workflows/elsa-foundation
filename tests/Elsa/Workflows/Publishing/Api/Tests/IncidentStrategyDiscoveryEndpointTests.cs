using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class IncidentStrategyDiscoveryEndpointTests
{
    [Fact]
    public void Endpoint_has_pinned_route_and_publishing_read_permission()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named("ListIncidentStrategies");

        Assert.Equal("publishing/incident-strategies", endpoint.RoutePattern.RawText?.TrimStart('/'));
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        Assert.Equal(PermissionKey.Normalize(WorkflowPublishingPermissions.Read),
            Assert.Single(Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor).Permissions));
    }

    [Fact]
    public async Task Handler_returns_the_effective_default_and_deterministically_orders_safe_descriptors()
    {
        var catalog = new StubCatalog(
        [
            Descriptor("zeta.Alert", "2", "Zeta", "Second"),
            Descriptor("Acme.Review", "2", "Review v2", null),
            Descriptor("Acme.Review", "1", "Review v1", "First")
        ], new IncidentStrategyReference("Fault", "1"));
        var handler = new ListIncidentStrategiesRequestHandler(catalog);

        var response = await handler.Handle(new ListIncidentStrategies(), CancellationToken.None);

        Assert.Equal(("Fault", "1"), (response.DefaultStrategy.Alias, response.DefaultStrategy.Version));
        Assert.Equal(
            [("Acme.Review", "1"), ("Acme.Review", "2"), ("zeta.Alert", "2")],
            response.Items.Select(item => (item.Alias, item.Version)));
        Assert.Equal("First", response.Items.First().Description);
    }

    [Fact]
    public async Task Handler_uses_catalog_descriptors_without_resolving_a_strategy_implementation()
    {
        var catalog = new StubCatalog(
            [Descriptor("Acme.Review", "1", "Review", null)],
            new IncidentStrategyReference("Fault", "1"));
        var handler = new ListIncidentStrategiesRequestHandler(catalog);

        _ = await handler.Handle(new ListIncidentStrategies(), CancellationToken.None);

        Assert.Equal(1, catalog.ListCallCount);
    }

    [Fact]
    public void Publishing_capability_advertises_the_untemplated_discovery_relation()
    {
        var link = Assert.Single(
            PublishingApiCapabilities.StaticDeclaration.Links,
            candidate => candidate.Rel == "incident-strategies");

        Assert.Equal("publishing/incident-strategies", link.Href);
        Assert.False(link.Templated);
    }

    private static IncidentStrategyDescriptor Descriptor(string alias, string version, string displayName, string? description) =>
        new(new IncidentStrategyReference(alias, version), displayName, description);

    private sealed class StubCatalog(
        IReadOnlyCollection<IncidentStrategyDescriptor> descriptors,
        IncidentStrategyReference defaultStrategy) : IIncidentStrategyCatalog
    {
        public int ListCallCount { get; private set; }

        public IncidentStrategyReference DefaultStrategy { get; } = defaultStrategy;

        public IReadOnlyCollection<IncidentStrategyDescriptor> List()
        {
            ListCallCount++;
            return descriptors;
        }

        public bool TryGet(IncidentStrategyReference reference, out IncidentStrategyDescriptor descriptor)
        {
            descriptor = descriptors.FirstOrDefault(candidate => candidate.Reference.Equals(reference))!;
            return descriptor is not null;
        }
    }
}
