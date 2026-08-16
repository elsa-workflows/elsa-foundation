using Elsa.Api.Compatibility.Testing.Baselines;
using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentBindingCompatibilityTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Minimal_api_preserves_fastendpoints_binding_failures_for_empty_malformed_json_and_invalid_take()
    {
        var baseline = BaselineFile.Load<BindingObservation[]>(Path.Join(BaselineDirectory, "wave4-agent-binding-fastendpoints.json"));
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        foreach (var expected in baseline)
        {
            using var request = CreateRequest(expected);
            using var response = await host.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(expected.StatusCode, (int)response.StatusCode);
            Assert.Equal(expected.ContentType, response.Content.Headers.ContentType?.ToString());
            Assert.Equal(expected.Body, body);
        }
    }

    private static HttpRequestMessage CreateRequest(BindingObservation observation)
    {
        var request = observation.Case switch
        {
            "audit-invalid-take" => new HttpRequestMessage(HttpMethod.Get, "/_elsa/agent/audit?take=not-an-int"),
            "create-empty-json" => new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions")
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            },
            "create-malformed-json" => new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions")
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json")
            },
            _ => throw new InvalidOperationException($"Unknown binding baseline case '{observation.Case}'.")
        };

        request.Headers.Add(Wave4AgentHost.IdentityHeader, observation.Case == "audit-invalid-take" ? "audit" : "use");
        return request;
    }

    private sealed record BindingObservation(
        string Case,
        string Endpoint,
        int StatusCode,
        string ContentType,
        string Body);
}
