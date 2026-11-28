using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpRequestModelTests
{
    [Fact]
    public void Wire_model_snapshots_mutable_collections_and_is_a_valid_typed_trigger()
    {
        var headerValues = new[] { "before" };
        var headers = new Dictionary<string, string[]> { ["X-Test"] = headerValues };
        var query = new Dictionary<string, string[]> { ["q"] = ["one"] };
        var routeData = new Dictionary<string, string> { ["id"] = "42" };

        var request = new HttpRequestModel("orders/42", "GET", headers, query, null, routeData);
        var registration = new ActivityTriggerRegistration<HttpRequestModel>("resume", "HttpEndpoint", "hash");
        headers["X-Test"] = ["replaced"];
        headerValues[0] = "mutated";
        query["q"][0] = "mutated";
        routeData["id"] = "99";

        Assert.Equal("before", request.Headers["X-Test"][0]);
        Assert.Equal("one", request.Query["q"][0]);
        Assert.Equal("42", request.RouteData["id"]);
        Assert.Contains(nameof(HttpRequestModel), registration.PayloadType.Alias, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, IReadOnlyList<string>>)request.Headers)["new"] = ["value"]);
    }
}
