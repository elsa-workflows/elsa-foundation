using Elsa.Api.Compatibility.Testing.Baselines;
using System.Net;
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

    [Fact]
    public async Task Minimal_api_route_values_override_conflicting_body_identifiers()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        using var message = await SendJsonAsync(host, HttpMethod.Post, "/_elsa/agent/sessions/session-1/messages",
            "{\"sessionId\":\"body-session\",\"message\":\"hello\"}");
        var messageBody = await message.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        Assert.Contains("\"sessionId\":\"session-1\"", messageBody, StringComparison.Ordinal);

        using var cancel = await SendJsonAsync(host, HttpMethod.Post, "/_elsa/agent/sessions/session-1/turns/turn-42/cancel",
            "{\"sessionId\":\"body-session\",\"turnId\":\"body-turn\"}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Contains("\"turnId\":\"turn-42\"", await cancel.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        foreach (var action in new[] { "approve", "deny", "execute" })
        {
            using var proposal = await SendJsonAsync(host, HttpMethod.Post, $"/_elsa/agent/proposals/proposal-1/{action}",
                "{\"proposalId\":\"body-proposal\"}");
            Assert.Equal(HttpStatusCode.OK, proposal.StatusCode);
        }
    }

    [Fact]
    public async Task Minimal_api_preserves_fastendpoints_null_and_unsupported_body_binding()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();

        using var literalNull = await SendJsonAsync(host, HttpMethod.Post, "/_elsa/agent/sessions/session-1/messages", "null");
        Assert.Equal(HttpStatusCode.OK, literalNull.StatusCode);
        Assert.Contains("\"sessionId\":\"session-1\"", await literalNull.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var absentContentType = new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions/session-1/messages")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"))
        };
        absentContentType.Headers.Add(Wave4AgentHost.IdentityHeader, "use");
        using var absent = await host.Client.SendAsync(absentContentType);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, absent.StatusCode);
        Assert.Null(absent.Content.Headers.ContentType);
        Assert.Equal(string.Empty, await absent.Content.ReadAsStringAsync());

        using var nonJson = new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions/session-1/messages")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain")
        };
        nonJson.Headers.Add(Wave4AgentHost.IdentityHeader, "use");
        using var nonJsonResponse = await host.Client.SendAsync(nonJson);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, nonJsonResponse.StatusCode);
        Assert.Null(nonJsonResponse.Content.Headers.ContentType);
        Assert.Equal(string.Empty, await nonJsonResponse.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(Wave4AgentMinimalApiHost host, HttpMethod method, string path, string body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "wildcard");
        return await host.Client.SendAsync(request);
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
