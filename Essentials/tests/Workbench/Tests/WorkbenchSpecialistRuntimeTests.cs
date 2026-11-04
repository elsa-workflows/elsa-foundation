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
/// model-turn workers and durable turn journal, and a draft Test Run's specialist evidence is read through the generic run's
/// identity and authorization. No model is called: the connection carries a placeholder key, the preflight reads only the
/// connection's declared capabilities, and the evidence run is refused before dispatch.
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
        var draftId = await CreateAgentDraftAsync(client);

        using var preflight = await client.PostAsync($"/design/activities/drafts/{draftId}/publication-preflight", Json(new JsonObject { ["expectedDraftRevision"] = 1 }));
        await AssertStatusAsync(HttpStatusCode.OK, preflight, "The publication preflight");
        var review = (await preflight.Content.ReadFromJsonAsync<JsonNode>())!;
        var consumer = review["runtime"]!.AsArray().SingleOrDefault(entry => (string?)entry!["kind"] == "RuntimeConsumer" && (string?)entry["key"] == ProviderKey);
        Assert.True((string?)consumer?["status"] == "Available", $"The specialist runtime consumer is not available: {review.ToJsonString()}");
        Assert.True((bool)review["isPublishable"]!, $"The agent draft is not publishable: {review.ToJsonString()}");
    }

    [Fact]
    public async Task Stock_development_shell_reads_specialist_evidence_through_the_generic_test_run()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        using var client = await SignedInClientAsync(workbench);
        var draftId = await CreateAgentDraftAsync(client);

        // A stale revision is refused before any dispatch, so the receipt exists and no model is called.
        using var started = await client.PostAsync($"/publishing/activity-drafts/{draftId}/test-runs",
            Json(new JsonObject { ["expectedRevision"] = 99, ["idempotencyKey"] = "evidence-stale" }));
        await AssertStatusAsync(HttpStatusCode.Accepted, started, "Starting the stale test run");
        var run = (await started.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("ValidationRejected", (string?)run["status"]);

        var testRunId = (string)run["testRunId"]!;
        using var evidence = await client.GetAsync($"/agent/specialists/test-runs/{testRunId}/evidence");
        await AssertStatusAsync(HttpStatusCode.OK, evidence, "Reading the test run evidence");
        var view = (await evidence.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(testRunId, (string?)view["testRunId"]);
        Assert.Equal(draftId, (string?)view["draftId"]);
        Assert.Equal("ValidationRejected", (string?)view["status"]);
        Assert.Null(view["invocation"]);

        using var unknown = await client.GetAsync("/agent/specialists/test-runs/not-a-test-run/evidence");
        await AssertStatusAsync(HttpStatusCode.NotFound, unknown, "Reading an unknown test run");
        Assert.Equal("activity.test-run.not-found", (string?)(await unknown.Content.ReadFromJsonAsync<JsonNode>())!["errorCode"]);

        using var anonymous = new HttpClient { BaseAddress = workbench.Client.BaseAddress };
        using var denied = await anonymous.GetAsync($"/agent/specialists/test-runs/{testRunId}/evidence");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private static async Task<string> CreateAgentDraftAsync(HttpClient client, string connectionName = "preflight-model",
        string endpoint = "https://api.anthropic.com/", string allowedHosts = "api.anthropic.com", bool withBrief = false)
    {
        using var connection = await client.PostAsync("/connections", Json(new JsonObject
        {
            ["name"] = connectionName,
            ["typeName"] = "specialist-anthropic",
            ["authScheme"] = "api-key",
            ["settings"] = new JsonObject
            {
                ["endpoint"] = endpoint,
                ["allowedHosts"] = allowedHosts,
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
                    ["model"] = new JsonObject { ["providerKey"] = "anthropic", ["connectionName"] = connectionName, ["modelId"] = "claude-fable-5-1" },
                    ["settings"] = new JsonObject(),
                    ["inputConstraints"] = new JsonObject(),
                    ["resultConstraints"] = new JsonObject(),
                    ["limits"] = new JsonObject { ["maximumModelCalls"] = 8, ["timeoutMilliseconds"] = 300000 }
                }
            },
            ["contract"] = new JsonObject
            {
                ["contractSchemaVersion"] = capabilities["contractSchemaVersions"]![0]!.DeepClone(),
                ["inputs"] = withBrief
                    ? new JsonArray(new JsonObject { ["referenceKey"] = "brief", ["name"] = "Brief", ["type"] = new JsonObject { ["alias"] = "String", ["collectionKind"] = "Single" },
                        ["isRequired"] = true, ["isNullable"] = false, ["default"] = null, ["storageDriverKey"] = "elsa.json" })
                    : new JsonArray(),
                ["outputs"] = new JsonArray(),
                ["outcomes"] = outcomes
            },
            ["layout"] = new JsonArray()
        }));
        await AssertStatusAsync(HttpStatusCode.Created, created, "Creating the agent");
        return (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["draft"]!["draftId"]!;

    }

    [Fact]
    public async Task Stock_development_shell_reads_a_real_invocations_evidence_by_test_run_and_by_occurrence()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        using var client = await SignedInClientAsync(workbench);
        // A closed local port: the model request is admitted and dispatched, then refused locally, so nothing leaves the machine.
        var draftId = await CreateAgentDraftAsync(client, "unreachable-model", "https://localhost:9/", "localhost", withBrief: true);

        using var started = await client.PostAsync($"/publishing/activity-drafts/{draftId}/test-runs", Json(new JsonObject
        {
            ["expectedRevision"] = 1, ["idempotencyKey"] = "evidence-real",
            ["inputs"] = new JsonObject { ["brief"] = new JsonObject { ["state"] = "Present", ["value"] = "moss gardens" } }
        }));
        await AssertStatusAsync(HttpStatusCode.Accepted, started, "Starting the test run");
        var testRunId = (string)(await started.Content.ReadFromJsonAsync<JsonNode>())!["testRunId"]!;

        JsonNode view = null!;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            view = (await client.GetFromJsonAsync<JsonNode>($"/agent/specialists/test-runs/{testRunId}/evidence"))!;
            if ((string?)view["status"] is "Completed" or "Faulted" or "Cancelled") break;
            await Task.Delay(500);
        }
        var invocation = view["invocation"];
        Assert.True(invocation is not null && (string?)view["status"] == "Faulted", $"The run did not fault with evidence: {view.ToJsonString()}");
        var turn = invocation!["turn"]!;
        Assert.Single(turn["modelRequests"]!.AsArray());
        Assert.Contains((string?)turn["status"], new[] { "OutcomeUnknown", "Failed" });
        Assert.Equal("draft-1", (string?)invocation["specialist"]!["version"]);
        Assert.Equal("unreachable-model", (string?)invocation["model"]!["connectionName"]);
        Assert.True((bool)view["valuesPermitted"]!);
        Assert.DoesNotContain("placeholder-not-a-key", view.ToJsonString(), StringComparison.Ordinal);

        var workflowExecutionId = (string)view["workflowExecutionId"]!;
        var activityExecutionId = (string)invocation["activityExecutionId"]!;
        using var occurrence = await client.GetAsync($"/agent/specialists/invocations/{workflowExecutionId}/{activityExecutionId}/evidence");
        await AssertStatusAsync(HttpStatusCode.OK, occurrence, "Reading the occurrence evidence");
        var read = (await occurrence.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(activityExecutionId, (string?)read["invocation"]!["activityExecutionId"]);
        Assert.Equal((string?)turn["status"], (string?)read["invocation"]!["turn"]!["status"]);
        Assert.DoesNotContain("placeholder-not-a-key", read.ToJsonString(), StringComparison.Ordinal);

        // Run inspection's state view names the occurrence as an agent invocation, which Studio links to its evidence page.
        var stateView = (await client.GetFromJsonAsync<JsonNode>($"/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view"))!;
        Assert.True((bool)stateView["available"]!, stateView.ToJsonString());
        Assert.Equal(("elsa.specialist.invocation", 1, true), ((string?)stateView["viewKind"], (int?)stateView["viewVersion"], (bool?)stateView["view"]!["invocation"]));

        using var other = await client.GetAsync($"/agent/specialists/invocations/{workflowExecutionId}/not-an-activity/evidence");
        await AssertStatusAsync(HttpStatusCode.NotFound, other, "Reading a non-specialist occurrence");
        using var missing = await client.GetAsync($"/agent/specialists/invocations/not-a-run/{activityExecutionId}/evidence");
        await AssertStatusAsync(HttpStatusCode.NotFound, missing, "Reading an unknown run");
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
