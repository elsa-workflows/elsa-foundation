using Elsa.Tagging.Api.Capabilities;
using Xunit;

namespace Elsa.Tagging.Tests;

public sealed class TaggingApiCapabilitiesTests
{
    [Fact]
    public void Advertises_the_v1_catalog_link_at_the_frozen_route()
    {
        var capability = TaggingApiCapabilities.StaticDeclaration;

        Assert.Equal("elsa.api.tagging", capability.CapabilityId);
        Assert.Equal(1, capability.ContractMajorVersion);
        var link = Assert.Single(capability.Links);
        Assert.Equal("tag-definitions", link.Rel);
        Assert.Equal("tagging/definitions", link.Href);
    }
}
