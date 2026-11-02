using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #3525 T032 on the stock Workbench: user-defined agents can be published, so the stock Development shell composes the
/// specialist runtime that publication preflight requires as the runtime consumer of <c>elsa.specialist</c>, with its
/// model-turn workers and durable turn journal. No model is called: the connection carries a placeholder key, and the
/// preflight reads only the connection's declared capabilities.
/// </summary>
public sealed class WorkbenchSpecialistRuntimeTests
{
    private const string ProviderKey = "elsa.specialist";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "Password123!";

    [Fact]
    public async Task Stock_development_shell_offers_the_specialist_runtime_to_publication_preflight()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        using var client = await SignedInClientAsync(workbench);

        using var connection = await client.PostAsync("/connections", Json(new JsonObject
        {
            ["name"] = "preflight-model",
            ["typeName"] = "specialist-anthropic",
            ["authScheme"] = "api-key",
            ["settings"] = new JsonObject
            {
                ["endpoint"] = "https://api.anthropic.com/",
                ["allowedHosts"] = "api.anthropic.com",
                ["modelId"] = "claude-fable-5-1",
                ["maximumOutputTokens"] = "4096",
                ["temperatureSupported"] = "true",
                ["structuredResults"] = "true"
            },
            ["secretFields"] = new JsonObject { ["apiKey"] = new JsonObject { ["value"] = "placeholder-not-a-key" } },
            ["owner"] = "tenant"
        }));
        await AssertStatusAsync(HttpStatusCode.Created, connection, "Creating the model connection");

        var capabilities = await client.GetFromJsonAsync<JsonNode>("/design/activities/authoring-capabilities");
        var outcomes = capabilities!["providers"]!.AsArray().Single(provider => (string?)provider!["providerKey"] == ProviderKey)!["requiredOutcomes"]!.DeepClone();
        using var created = await client.PostAsync("/design/activities/definitions", Json(new JsonObject
        {
            ["category"] = "Agents",
            ["displayName"] = "Preflight agent",
            ["provider"] = new JsonObject
            {
                ["providerKey"] = ProviderKey,
                ["schemaVersion"] = "1",
                ["payload"] = new JsonObject
                {
                    ["instructions"] = "Answer briefly.",
                    ["model"] = new JsonObject { ["providerKey"] = "anthropic", ["connectionName"] = "preflight-model", ["modelId"] = "claude-fable-5-1" },
                    ["settings"] = new JsonObject(),
                    ["inputConstraints"] = new JsonObject(),
                    ["resultConstraints"] = new JsonObject(),
                    ["limits"] = new JsonObject { ["maximumModelCalls"] = 8, ["timeoutMilliseconds"] = 300000 }
                }
            },
            ["contract"] = new JsonObject
            {
                ["contractSchemaVersion"] = capabilities["contractSchemaVersions"]![0]!.DeepClone(),
                ["inputs"] = new JsonArray(),
                ["outputs"] = new JsonArray(),
                ["outcomes"] = outcomes
            },
            ["layout"] = new JsonArray()
        }));
        await AssertStatusAsync(HttpStatusCode.Created, created, "Creating the agent");
        var draftId = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["draft"]!["draftId"]!;

        using var preflight = await client.PostAsync($"/design/activities/drafts/{draftId}/publication-preflight", Json(new JsonObject { ["expectedDraftRevision"] = 1 }));
        await AssertStatusAsync(HttpStatusCode.OK, preflight, "The publication preflight");
        var review = (await preflight.Content.ReadFromJsonAsync<JsonNode>())!;
        var consumer = review["runtime"]!.AsArray().SingleOrDefault(entry => (string?)entry!["kind"] == "RuntimeConsumer" && (string?)entry["key"] == ProviderKey);
        Assert.True((string?)consumer?["status"] == "Available", $"The specialist runtime consumer is not available: {review.ToJsonString()}");
        Assert.True((bool)review["isPublishable"]!, $"The agent draft is not publishable: {review.ToJsonString()}");
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response, string step) =>
        Assert.True(response.StatusCode == expected, $"{step} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<HttpClient> SignedInClientAsync(WorkbenchProcess workbench)
    {
        var client = new HttpClient { BaseAddress = workbench.Client.BaseAddress };
        try
        {
            using var login = await client.PostAsync("/_elsa/identity/login", Json(new JsonObject { ["username"] = AdminUsername, ["password"] = AdminPassword }));
            Assert.True(login.IsSuccessStatusCode, $"Admin sign-in failed with {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
            var token = await client.GetFromJsonAsync<JsonNode>("/_elsa/identity/token");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)token!["accessToken"]!);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
