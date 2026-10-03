using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>Exercises expression tooling through the persisted, authenticated Workbench authoring path.</summary>
public sealed class ExpressionToolingHostEvidenceTests
{
    private const string ReadLineTypeKey = "Elsa.Activities.Primitives.Activities.ReadLine";
    private const string WriteLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string SequenceTypeKey = "Elsa.Activities.Sequence.Activities.Sequence";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stock_workbench_persists_draft_and_composes_Javascript_and_Liquid_tooling(bool rotateBearer)
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.True(catalog.TryGetValue("JavaScriptExpressions", out var javascript) && javascript.Runs);
        Assert.True(catalog.TryGetValue("Liquid", out var liquid) && liquid.Runs);
        Assert.True(catalog.TryGetValue("WorkflowsDesignEntityFrameworkCore", out var designPersistence) && designPersistence.Runs);

        using var clientHandler = new HttpClientHandler { UseCookies = true };
        using var client = new HttpClient(clientHandler) { BaseAddress = workbench.Client.BaseAddress };
        using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var bearerHandler = new HttpClientHandler { UseCookies = false };
        using var bearerClient = new HttpClient(bearerHandler) { BaseAddress = client.BaseAddress };
        var issuedTokenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        async Task<HttpResponseMessage> PostToolingAsync(string path, object body)
        {
            if (!rotateBearer)
                return await client.PostAsJsonAsync(path, body);

            // Match Studio's normal cookie-to-bearer exchange on every API operation. Never persist or log tokens.
            using var tokenResponse = await client.GetAsync("/_elsa/identity/token");
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            var token = tokenDocument.RootElement.GetProperty("accessToken").GetString()!;
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
            Assert.True(issuedTokenFingerprints.Add(fingerprint), "The bearer regression must issue a different token for every tooling operation.");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await bearerClient.SendAsync(request);
        }

        var toolingLinks = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var capabilityResponse = await client.GetAsync("/capabilities"))
        {
            Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
            using var capabilities = JsonDocument.Parse(await capabilityResponse.Content.ReadAsStringAsync());
            var tooling = Assert.Single(capabilities.RootElement.GetProperty("capabilities").EnumerateArray(), capability =>
                capability.GetProperty("id").GetString() == "expressions.tooling.v1");
            foreach (var relation in new[] { "expression-tooling-context", "expression-tooling-completions", "expression-tooling-hover", "expression-tooling-validate" })
            {
                var link = Assert.Single(tooling.GetProperty("links").EnumerateArray(), link => link.GetProperty("rel").GetString() == relation);
                var href = link.GetProperty("href").GetString();
                Assert.False(string.IsNullOrWhiteSpace(href));
                toolingLinks.Add(relation, href!);
            }
        }

        var readLine = await ReadActivityVersionAsync(client, ReadLineTypeKey);
        var writeLine = await ReadActivityVersionAsync(client, WriteLineTypeKey);
        var sequence = await ReadActivityVersionAsync(client, SequenceTypeKey);
        var output = Assert.Single(readLine.ContractOutputs);
        Assert.Equal("Line", output.Name);
        Assert.Equal("String", output.Type);
        var textInput = Assert.Single(writeLine.ContractInputs);
        Assert.Equal("text", textInput.ReferenceKey);
        Assert.Equal("Text", textInput.Name);
        Assert.Equal("String", textInput.Type);

        using var definitionResponse = await client.PostAsJsonAsync("/design/workflows/definitions", new
        {
            name = "Expression tooling persisted draft",
            initialState = new
            {
                inputs = new[] { new { referenceKey = "customer-key", name = "customerName", type = new { alias = "String", collectionKind = "single" }, displayName = "Customer name", isNullable = true } },
                variables = new[] { new { referenceKey = "global-key", name = "globalLabel", type = new { alias = "String", collectionKind = "single" }, storageDriverType = (string?)null, @default = (object?)null } },
                rootActivity = new
                {
                    nodeId = "root",
                    activityVersionId = sequence.VersionId,
                    inputs = Array.Empty<object>(),
                    outputs = Array.Empty<object>(),
                    structure = new
                    {
                        kind = "elsa.sequence.structure",
                        schemaVersion = "1.0.0",
                        payload = new
                        {
                            variables = new[] { new { referenceKey = "scoped-key", name = "scopedLabel", type = new { alias = "String", collectionKind = "single" }, storageDriverType = (string?)null, @default = (object?)null } },
                            activities = new object[]
                            {
                                new { nodeId = "predecessor", activityVersionId = readLine.VersionId, inputs = Array.Empty<object>(), outputs = Array.Empty<object>() },
                                new { nodeId = "target", activityVersionId = writeLine.VersionId, inputs = new[] { new { referenceKey = textInput.ReferenceKey, value = new { value = "", expressionType = "JavaScript" }, autoEvaluate = (bool?)null, evaluatorType = (string?)null, storageDriverType = (string?)null, isSensitive = (bool?)null } }, outputs = Array.Empty<object>() }
                            }
                        }
                    }
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, definitionResponse.StatusCode);
        using var definition = JsonDocument.Parse(await definitionResponse.Content.ReadAsStringAsync());
        var draftId = definition.RootElement.GetProperty("draft").GetProperty("id").GetString()!;

        // A fresh HTTP request resolves the newly persisted draft through the production EF-backed store.
        using var reopenedResponse = await client.GetAsync($"/design/workflows/drafts/{Uri.EscapeDataString(draftId)}");
        Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
        using var reopened = JsonDocument.Parse(await reopenedResponse.Content.ReadAsStringAsync());
        Assert.Equal(draftId, reopened.RootElement.GetProperty("id").GetString());
        Assert.Equal("target", reopened.RootElement.GetProperty("state").GetProperty("rootActivity").GetProperty("structure").GetProperty("payload").GetProperty("activities")[1].GetProperty("nodeId").GetString());
        Assert.True(File.Exists(Path.Combine(workbench.ContentRoot, "elsa.db")), "The real Workbench EF design persistence resource did not create elsa.db.");

        using (var anonymous = new HttpClient { BaseAddress = workbench.Client.BaseAddress })
        using (var denied = await anonymous.PostAsJsonAsync(toolingLinks["expression-tooling-context"], ContextBody(draftId, "JavaScript", textInput.ReferenceKey)))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        foreach (var syntax in new[] { "JavaScript", "Liquid" })
        {
            using var contextResponse = await PostToolingAsync(toolingLinks["expression-tooling-context"], ContextBody(draftId, syntax, textInput.ReferenceKey));
            Assert.Equal(HttpStatusCode.OK, contextResponse.StatusCode);
            using var contextDocument = JsonDocument.Parse(await contextResponse.Content.ReadAsStringAsync());
            var result = contextDocument.RootElement.GetProperty("result");
            Assert.Equal(0, result.GetProperty("state").GetInt32());
            var payload = result.GetProperty("payload");
            var contextRevision = AssertRevisionEnvelope(result);
            Assert.Equal("persisted-draft", payload.GetProperty("document").GetProperty("documentRevision").GetString());
            Assert.Equal(contextRevision, payload.GetProperty("contextRevision").GetString());
            var symbols = payload.GetProperty("rootSymbols").EnumerateArray().ToArray();
            Assert.Contains(symbols, symbol => symbol.GetProperty("name").GetString() == "customerName");
            Assert.Contains(symbols, symbol => symbol.GetProperty("name").GetString() == "globalLabel");
            Assert.Contains(symbols, symbol => symbol.GetProperty("name").GetString() == "scopedLabel");
            var predecessorOutput = Assert.Single(symbols, symbol => symbol.GetProperty("name").GetString() == $"predecessor.{output.Name}");
            Assert.Equal(output.Type, predecessorOutput.GetProperty("valueShape").GetProperty("displayName").GetString());

            var completionSource = syntax == "JavaScript" ? "args.predecessor." : "predecessor.";
            using var completionResponse = await PostToolingAsync(toolingLinks["expression-tooling-completions"], SourceBody(draftId, syntax, textInput.ReferenceKey, completionSource, contextRevision, completionSource.Length));
            Assert.Equal(HttpStatusCode.OK, completionResponse.StatusCode);
            using var completionDocument = JsonDocument.Parse(await completionResponse.Content.ReadAsStringAsync());
            var completionResult = completionDocument.RootElement.GetProperty("result");
            Assert.Equal(0, completionResult.GetProperty("state").GetInt32());
            AssertRevisionEnvelope(completionResult, contextRevision);
            var outputCompletion = Assert.Single(completionResult.GetProperty("payload").GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == output.Name);
            Assert.Equal(output.Type, outputCompletion.GetProperty("detail").GetString());

            var hoverSource = syntax == "JavaScript" ? $"args.predecessor.{output.Name}" : $"predecessor.{output.Name}";
            using var hoverResponse = await PostToolingAsync(toolingLinks["expression-tooling-hover"], SourceBody(draftId, syntax, textInput.ReferenceKey, hoverSource, contextRevision, hoverSource.Length, hover: true));
            Assert.Equal(HttpStatusCode.OK, hoverResponse.StatusCode);
            using var hoverDocument = JsonDocument.Parse(await hoverResponse.Content.ReadAsStringAsync());
            var hoverResult = hoverDocument.RootElement.GetProperty("result");
            Assert.Equal(0, hoverResult.GetProperty("state").GetInt32());
            AssertRevisionEnvelope(hoverResult, contextRevision);
            var hoverContents = hoverResult.GetProperty("payload").GetProperty("contents").GetString()!;
            Assert.Contains(output.Name, hoverContents, StringComparison.Ordinal);
            Assert.Contains("Activity output", hoverContents, StringComparison.Ordinal);

            var invalid = syntax == "JavaScript" ? "if (" : "{{ customerName";
            using var validationResponse = await PostToolingAsync(toolingLinks["expression-tooling-validate"], SourceBody(draftId, syntax, textInput.ReferenceKey, invalid, contextRevision));
            Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
            using var validationDocument = JsonDocument.Parse(await validationResponse.Content.ReadAsStringAsync());
            var validationResult = validationDocument.RootElement.GetProperty("result");
            Assert.Equal(0, validationResult.GetProperty("state").GetInt32());
            AssertRevisionEnvelope(validationResult, contextRevision);
            Assert.NotEmpty(validationResult.GetProperty("payload").GetProperty("diagnostics").EnumerateArray());

            if (syntax != "JavaScript") continue;

            // The composed provider must describe the same deterministic runtime surface,
            // through real advertised routes and both cookie and rotating-bearer paths.
            const string mathPrefix = "Math.";
            using var mathResponse = await PostToolingAsync(toolingLinks["expression-tooling-completions"],
                SourceBody(draftId, syntax, textInput.ReferenceKey, mathPrefix, contextRevision, mathPrefix.Length));
            Assert.Equal(HttpStatusCode.OK, mathResponse.StatusCode);
            using var mathDocument = JsonDocument.Parse(await mathResponse.Content.ReadAsStringAsync());
            var mathResult = mathDocument.RootElement.GetProperty("result");
            Assert.Equal(0, mathResult.GetProperty("state").GetInt32());
            AssertRevisionEnvelope(mathResult, contextRevision);
            var mathItems = mathResult.GetProperty("payload").GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(mathItems, item => item.GetProperty("label").GetString() == "abs");
            Assert.DoesNotContain(mathItems, item => item.GetProperty("label").GetString() == "random");

            foreach (var (source, diagnosticCode) in new[]
            {
                ("(value: number) => value", "JavaScript/Syntax"),
                ("<span />", "JavaScript/Syntax"),
                ("const value = 1; value", "JavaScript/Syntax"),
                ("Math.random()", "JavaScript/AmbientCapability"),
                ("(() => { return JSON.stringify({ total: Math.abs(-2) }); })()", (string?)null)
            })
            {
                using var profileResponse = await PostToolingAsync(toolingLinks["expression-tooling-validate"],
                    SourceBody(draftId, syntax, textInput.ReferenceKey, source, contextRevision));
                Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
                using var profileDocument = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
                var profileResult = profileDocument.RootElement.GetProperty("result");
                AssertRevisionEnvelope(profileResult, contextRevision);
                var diagnostics = profileResult.GetProperty("payload").GetProperty("diagnostics").EnumerateArray().ToArray();
                if (diagnosticCode is null)
                {
                    Assert.Equal(1, profileResult.GetProperty("state").GetInt32());
                    Assert.Empty(diagnostics);
                }
                else
                {
                    Assert.Equal(0, profileResult.GetProperty("state").GetInt32());
                    Assert.Contains(diagnostics, diagnostic => diagnostic.GetProperty("code").GetString() == diagnosticCode);
                }
            }
        }
    }

    [Fact]
    public async Task Stock_workbench_without_Liquid_feature_does_not_describe_a_Liquid_provider()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, directory =>
        {
            var path = Path.Combine(directory, "shells.json");
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            root["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject().Remove("Liquid");
            File.WriteAllText(path, root.ToJsonString());
        });

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.False(catalog["Liquid"].Runs);
        Assert.True(catalog["JavaScriptExpressions"].Runs);
        using var clientHandler = new HttpClientHandler { UseCookies = true };
        using var client = new HttpClient(clientHandler) { BaseAddress = workbench.Client.BaseAddress };
        using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var descriptorResponse = await client.GetAsync("/design/workflows/expression-tooling/descriptors");
        Assert.Equal(HttpStatusCode.OK, descriptorResponse.StatusCode);
        using var descriptorDocument = JsonDocument.Parse(await descriptorResponse.Content.ReadAsStringAsync());
        var descriptors = descriptorDocument.RootElement.GetProperty("result").GetProperty("payload").EnumerateArray().ToArray();
        Assert.Contains(descriptors, descriptor => descriptor.GetProperty("expressionType").GetString() == "JavaScript");
        Assert.DoesNotContain(descriptors, descriptor => descriptor.GetProperty("expressionType").GetString() == "Liquid");
    }

    private static async Task<ActivityVersion> ReadActivityVersionAsync(HttpClient client, string activityTypeKey)
    {
        using var definitionsResponse = await client.GetAsync($"/design/activities/definitions?search={Uri.EscapeDataString(activityTypeKey)}");
        definitionsResponse.EnsureSuccessStatusCode();
        using var definitions = JsonDocument.Parse(await definitionsResponse.Content.ReadAsStringAsync());
        var definition = definitions.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("definition"))
            .Single(item => item.GetProperty("activityTypeKey").GetString() == activityTypeKey);
        var versionId = definition.GetProperty("headVersionId").GetString()!;
        using var versionResponse = await client.GetAsync($"/design/activities/versions/{Uri.EscapeDataString(versionId)}");
        versionResponse.EnsureSuccessStatusCode();
        using var version = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());
        var root = version.RootElement;
        var contract = root.GetProperty("contract");
        var inputs = contract.GetProperty("inputs").EnumerateArray()
            .Select(item => new ActivityInput(item.GetProperty("referenceKey").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("type").GetProperty("alias").GetString()!))
            .ToArray();
        var outputs = contract.GetProperty("outputs").EnumerateArray()
            .Select(item => new ActivityOutput(item.GetProperty("referenceKey").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("type").GetProperty("alias").GetString()!))
            .ToArray();
        return new(versionId, inputs, outputs);
    }

    private static object ContextBody(string draftId, string syntax, string propertyKey) => new
    {
        contractVersion = new { major = 1, minor = 0 },
        workflowDraftId = draftId,
        nodeId = "target",
        propertyKey,
        expressionType = syntax,
        documentRevision = "persisted-draft"
    };

    private static object SourceBody(string draftId, string syntax, string propertyKey, string source, string? contextRevision, int cursor = 0, bool hover = false) => new
    {
        contractVersion = new { major = 1, minor = 0 },
        workflowDraftId = draftId,
        nodeId = "target",
        propertyKey,
        expressionType = syntax,
        documentRevision = "persisted-draft",
        contextRevision,
        source,
        cursor = hover ? null : new { line = 0, character = cursor },
        position = hover ? new { line = 0, character = cursor } : null
    };

    private static string AssertRevisionEnvelope(JsonElement outcome, string? expectedContextRevision = null)
    {
        Assert.Equal("persisted-draft", outcome.GetProperty("documentRevision").GetString());
        var contextRevision = outcome.GetProperty("contextRevision").GetString();
        Assert.False(string.IsNullOrWhiteSpace(contextRevision));
        if (expectedContextRevision is not null)
            Assert.Equal(expectedContextRevision, contextRevision);
        return contextRevision!;
    }

    private sealed record ActivityVersion(string VersionId, IReadOnlyList<ActivityInput> ContractInputs, IReadOnlyList<ActivityOutput> ContractOutputs);
    private sealed record ActivityInput(string ReferenceKey, string Name, string Type);
    private sealed record ActivityOutput(string ReferenceKey, string Name, string Type);
}
