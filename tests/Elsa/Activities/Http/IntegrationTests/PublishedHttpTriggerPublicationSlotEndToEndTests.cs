using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Workflows.Runtime.Http.Exceptions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Acceptance evidence for publication-slot HTTP authority. These tests use publication-scoped bindings, the
/// production trigger indexer/validator/router/middleware, and the real Runtime HTTP observer/resolver; only the
/// route table implementation remains the fixture's production-equivalent test double.
/// </summary>
public sealed class PublishedHttpTriggerPublicationSlotEndToEndTests : IAsyncLifetime
{
    private const string DefaultSlot = "definition-orders:default";
    private const string ResultValueId = "publication-endpoint-result";
    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await HttpEndpointHostFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task PublicationSlot_DefaultReplacement_SwitchesFooToBar_ForNewStarts()
    {
        await PrepareAsync("publication-foo", DefaultSlot, "artifact-foo", "foo");
        await _fixture.ActivatePublishedHttpTriggerAsync("publication-foo", null, "artifact-foo");
        Assert.Equal(HttpStatusCode.Accepted, (await GetAsync("foo")).StatusCode);

        await PrepareAsync("publication-bar", DefaultSlot, "artifact-bar", "bar");

        // Preparation is durable but non-authoritative: the old route still starts and the candidate cannot.
        Assert.Equal(HttpStatusCode.Accepted, (await GetAsync("foo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("bar")).StatusCode);

        await _fixture.ActivatePublishedHttpTriggerAsync("publication-bar", "publication-foo", "artifact-bar");

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("foo")).StatusCode);
        var barResponse = await GetAsync("bar");
        Assert.Equal(HttpStatusCode.Accepted, barResponse.StatusCode);
        var state = await _fixture.WorkflowExecutionAsync(Assert.Single(await ReadStartedIdsAsync(barResponse)));
        Assert.Equal("reference:publication-bar", state.PinnedSource!.SourceReferenceId);
        Assert.Equal("publication-bar", state.PinnedSource.PublicationId);
        Assert.Equal(DefaultSlot, state.PinnedSource.SlotId);
    }

    [Fact]
    public async Task PublishedHttpTrigger_FailedCandidateActivation_PreservesFooAuthority()
    {
        await PrepareAsync("publication-foo", DefaultSlot, "artifact-foo", "foo");
        await _fixture.ActivatePublishedHttpTriggerAsync("publication-foo", null, "artifact-foo");

        await PrepareAsync("publication-bar", DefaultSlot, "artifact-bar", "bar");
        await _fixture.RemovePreparedPublishedHttpTriggerAsync("publication-bar");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.ActivatePublishedHttpTriggerAsync("publication-bar", "publication-foo", "artifact-bar"));

        var fooResponse = await GetAsync("foo");
        Assert.Equal(HttpStatusCode.Accepted, fooResponse.StatusCode);
        Assert.Single(await ReadStartedIdsAsync(fooResponse));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("bar")).StatusCode);
    }

    [Fact]
    public async Task PublicationSlot_NamedSlot_CoexistsForDistinctRoute_ButExclusiveDuplicateConflicts()
    {
        await PrepareAsync("publication-foo", DefaultSlot, "artifact-foo", "foo");
        await _fixture.ActivatePublishedHttpTriggerAsync("publication-foo", null, "artifact-foo");

        await PrepareAsync("publication-bar", "definition-orders:blue", "artifact-bar", "bar");
        await _fixture.ActivatePublishedHttpTriggerAsync("publication-bar", null, "artifact-bar");

        Assert.Equal(HttpStatusCode.Accepted, (await GetAsync("foo")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await GetAsync("bar")).StatusCode);

        // HTTP declares Exclusive cardinality. A second named slot cannot claim the same normalized route/method.
        await Assert.ThrowsAsync<EndpointRoutingConflictException>(() =>
            PrepareAsync("publication-conflict", "definition-orders:green", "artifact-conflict", "foo"));
        Assert.Empty(await _fixture.ListTriggerBindingsAsync("artifact-conflict"));
    }

    [Fact]
    public async Task PublishedHttpTrigger_RetiredOldStartProjection_DoesNotBreakPinnedWaitingExecution()
    {
        await _fixture.PublishSequencedHttpEndpointsWorkflowAsync(
            "artifact-old",
            "foo",
            "old-start-result",
            "old/callback",
            "old-callback-result",
            "POST");

        var startResponse = await _fixture.Client.PostAsync(
            "/workflows/http/foo",
            new StringContent("""{"phase":"start"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var oldExecutionId = Assert.Single(await ReadStartedIdsAsync(startResponse));
        Assert.Single(await _fixture.ListBookmarksAsync(oldExecutionId));

        // Retire only the old start projection. Its executable and the waiting bookmark remain durable.
        await _fixture.RetireArtifactTriggerAsync("artifact-old");
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-new", "bar", ResultValueId, "GET");
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("foo")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await GetAsync("bar")).StatusCode);

        var callbackResponse = await _fixture.Client.PostAsync(
            "/workflows/http/old/callback",
            new StringContent("""{"phase":"callback"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, callbackResponse.StatusCode);
        Assert.Equal(oldExecutionId, Assert.Single(await ReadResumedIdsAsync(callbackResponse)));

        var captured = await _fixture.ReadResultProjectionAsync(oldExecutionId, "Request");
        Assert.Equal("old/callback", HttpEndpointHostFixture.DeserializeRequest(captured).Path);
    }

    private Task PrepareAsync(string activationId, string slotId, string artifactId, string path) =>
        _fixture.PreparePublishedHttpTriggerAsync(
            activationId,
            slotId,
            artifactId,
            path,
            ResultValueId,
            "GET");

    private Task<HttpResponseMessage> GetAsync(string path) =>
        _fixture.Client.GetAsync($"/workflows/http/{path}");

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response) =>
        await ReadIdsAsync(response, "started");

    private static async Task<IReadOnlyList<string>> ReadResumedIdsAsync(HttpResponseMessage response) =>
        await ReadIdsAsync(response, "resumed");

    private static async Task<IReadOnlyList<string>> ReadIdsAsync(HttpResponseMessage response, string property)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        return payload.RootElement.GetProperty(property).EnumerateArray().Select(x => x.GetString()!).ToArray();
    }
}
