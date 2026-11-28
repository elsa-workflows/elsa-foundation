using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Decisions.Api;
using Elsa.Decisions.Design;
using Elsa.Decisions.Persistence.Core;

namespace Elsa.Decisions.Api.Tests;

public sealed class DecisionSavedCaseTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Saved_cases_run_against_exact_draft_or_published_candidate(bool published, bool matches)
    {
        await using var fixture = await ApiFixture.Create();
        var asset = await fixture.CreateAsset();
        using var save = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/cases",
            new SaveDecisionCaseRequest("case", "Under age boy", ApiFixture.Input("20"), "Success", ApiFixture.Expected(matches ? "Automatic" : "Manager")));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var item = (await save.Content.ReadFromJsonAsync<DecisionCaseView>())!;
        DecisionVersionView? version = null;
        if (published)
        {
            using var publication = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/publish", new PublishDecisionRequest("publish", asset.Revision));
            Assert.Equal(HttpStatusCode.OK, publication.StatusCode);
            version = (await publication.Content.ReadFromJsonAsync<DecisionVersionView>())!;
        }
        using var response = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/test",
            new TestDecisionRequest("test", version?.SourceHash ?? asset.SourceHash, [new(item.Id, item.Revision)], published ? null : asset.Revision, version?.Id));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var run = (await response.Content.ReadFromJsonAsync<DecisionTestReceipt>())!;
        Assert.Equal(matches, run.Passed);
        Assert.Equal("Success", Assert.Single(run.Comparisons).Status);
    }

    [Fact]
    public async Task Unsupported_draft_reports_safe_diagnostics_and_corrected_draft_runs()
    {
        await using var fixture = await ApiFixture.Create();
        var asset = await fixture.CreateAsset();
        const string privateFragment = "SECRET-SYNTAX";
        var invalidXml = ApiFixture.Xml.Replace("<text>&lt;= 1000</text>", $"<text>{privateFragment}</text>", StringComparison.Ordinal);
        using var update = await fixture.Send(HttpMethod.Put, $"/decisions/{asset.Id}", new SaveDecisionRequest("invalid-draft", asset.Name, invalidXml, ExpectedRevision: asset.Revision));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var draft = (await update.Content.ReadFromJsonAsync<DecisionAssetView>())!;
        using var saved = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/cases",
            new SaveDecisionCaseRequest("case", "Under age boy", ApiFixture.Input("950"), "Success", ApiFixture.Expected("Automatic")));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var item = (await saved.Content.ReadFromJsonAsync<DecisionCaseView>())!;

        using var failed = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/test",
            new TestDecisionRequest("invalid-run", draft.SourceHash, [new(item.Id, item.Revision)], draft.Revision));
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        var body = await failed.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("unsupported", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains("exact candidate", problem.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        var diagnostic = Assert.Single(problem.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray());
        Assert.Equal("automatic", diagnostic.GetProperty("ruleId").GetString());
        Assert.Equal("amount-column", diagnostic.GetProperty("clauseId").GetString());
        Assert.DoesNotContain(privateFragment, body);

        using var corrected = await fixture.Send(HttpMethod.Put, $"/decisions/{asset.Id}", new SaveDecisionRequest("corrected-draft", asset.Name, ApiFixture.Xml, ExpectedRevision: draft.Revision));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var correctedDraft = (await corrected.Content.ReadFromJsonAsync<DecisionAssetView>())!;
        using var response = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/test",
            new TestDecisionRequest("corrected-run", correctedDraft.SourceHash, [new(item.Id, item.Revision)], correctedDraft.Revision));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<DecisionTestReceipt>())!;
        Assert.True(run.Passed);
        Assert.Equal("Success", Assert.Single(run.Comparisons).Status);

        using var history = await fixture.Send(HttpMethod.Get, $"/decisions/{asset.Id}/runs");
        var receipt = Assert.Single((await history.Content.ReadFromJsonAsync<DecisionRunsResponse>())!.Items);
        Assert.Equal(run.Id, receipt.Id);
        Assert.True(receipt.Passed);
    }

    [Fact]
    public async Task Unsupported_sensitive_draft_does_not_disclose_compilation_diagnostics()
    {
        await using var fixture = await ApiFixture.Create();
        var asset = await fixture.CreateAsset(sensitive: true);
        var invalidXml = ApiFixture.Xml.Replace("<text>&lt;= 1000</text>", "<text>SECRET-SYNTAX</text>", StringComparison.Ordinal);
        using var update = await fixture.Send(HttpMethod.Put, $"/decisions/{asset.Id}", new SaveDecisionRequest("invalid-sensitive", asset.Name, invalidXml, ExpectedRevision: asset.Revision, IsSensitive: true));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var draft = (await update.Content.ReadFromJsonAsync<DecisionAssetView>())!;
        using var saved = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/cases",
            new SaveDecisionCaseRequest("case", "Protected case", ApiFixture.Input("950"), "Success", ApiFixture.Expected("Automatic")));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var item = (await saved.Content.ReadFromJsonAsync<DecisionCaseView>())!;

        using var response = await fixture.Send(HttpMethod.Post, $"/decisions/{asset.Id}/test",
            new TestDecisionRequest("protected-run", draft.SourceHash, [new(item.Id, item.Revision)], draft.Revision), grants: "decisions:read,decisions:test");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.False(problem.RootElement.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain("SECRET-SYNTAX", body);
    }
}
