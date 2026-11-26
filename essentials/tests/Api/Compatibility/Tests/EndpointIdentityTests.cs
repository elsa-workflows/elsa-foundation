using Elsa.Api.Compatibility.Testing.Manifests;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class EndpointIdentityTests
{
    [Fact]
    public void Route_and_method_identity_is_normalized_and_immutable()
    {
        var first = new EndpointIdentity("//API/Orders/{orderId:int}/", " get ");
        var equivalent = new EndpointIdentity("/api/orders/{differentName:int}", "GET");

        Assert.Equal(first, equivalent);
        Assert.Equal("/api/orders/{param:int}", first.Route.Value);
        Assert.Equal("GET", first.Method.Value);
        Assert.Equal("GET /api/orders/{param:int}", first.ToString());
    }
}
