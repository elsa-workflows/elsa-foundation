using System.Net;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Baselines;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentSseErrorCompatibilityTests
{
    private static readonly string BaselinePath = Path.Join(AppContext.BaseDirectory, "Baselines", "wave4-agent-sse-error-fastendpoints.json");

    [Fact]
    public async Task Minimal_api_preserves_generated_error_event_identity_and_utc_timestamp()
    {
        using var baseline = JsonDocument.Parse(BaselineFile.Read(BaselinePath));
        var expected = baseline.RootElement.GetProperty("event");
        var before = DateTimeOffset.UtcNow;

        await using var host = await Wave4AgentMinimalApiHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream");
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use|actor-2|tenant-1");
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var after = DateTimeOffset.UtcNow;
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal((HttpStatusCode)baseline.RootElement.GetProperty("statusCode").GetInt32(), response.StatusCode);
        Assert.Equal(baseline.RootElement.GetProperty("contentType").GetString(), response.Content.Headers.ContentType?.MediaType);
        foreach (var header in baseline.RootElement.GetProperty("headers").EnumerateObject())
            Assert.Equal(header.Value.GetString(), response.Headers.GetValues(header.Name).Single());

        Assert.StartsWith("data: ", body, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", body, StringComparison.Ordinal);
        using var actual = JsonDocument.Parse(body["data: ".Length..].Trim());
        Assert.Equal(expected.GetProperty("kind").GetInt32(), actual.RootElement.GetProperty("Kind").GetInt32());
        Assert.Null(actual.RootElement.GetProperty("Content").GetString());
        Assert.Null(actual.RootElement.GetProperty("ProposalId").GetString());
        Assert.Equal(expected.GetProperty("error").GetProperty("code").GetString(), actual.RootElement.GetProperty("Error").GetProperty("Code").GetString());
        Assert.Equal(expected.GetProperty("error").GetProperty("message").GetString(), actual.RootElement.GetProperty("Error").GetProperty("Message").GetString());
        Assert.Equal(expected.GetProperty("error").GetProperty("statusCode").GetInt32(), actual.RootElement.GetProperty("Error").GetProperty("StatusCode").GetInt32());
        Assert.Null(actual.RootElement.GetProperty("ResultKind").GetString());
        Assert.Null(actual.RootElement.GetProperty("Payload").GetString());

        var id = actual.RootElement.GetProperty("Id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.NotEqual(expected.GetProperty("id").GetString(), id);
        var createdAt = actual.RootElement.GetProperty("CreatedAt").GetDateTimeOffset();
        Assert.InRange(createdAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
