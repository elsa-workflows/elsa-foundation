using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using Xunit;

using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Verifies that the literal <c>workflows/drafts/test-runs</c> route is selected deterministically
/// over the parameterized <c>workflows/{versionId}/test-runs</c> route. Both endpoints are hosted
/// through the production Minimal API mapper so the test exercises the real route matcher.
/// </summary>
[Collection(nameof(WorkflowDraftTestRunRoutingTests))]
public sealed class WorkflowDraftTestRunRoutingTests : IAsyncLifetime
{
    private const string DraftPath = "/publishing/workflows/drafts/test-runs";

    private readonly RecordingRequestSender _sender = new();
    private CaptureWorkflowTestRunStarter _starter = null!;
    private PublishingMinimalApiHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await PublishingMinimalApiHost.StartAsync(_ => _sender);
        _starter = _host.App.Services.GetRequiredService<CaptureWorkflowTestRunStarter>();
        _client = _host.Client;
        _client.DefaultRequestHeaders.TryAddWithoutValidation(
            PublishingCompatibilityCases.IdentityHeader,
            "trusted");
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task DraftTestRunPathInvokesDraftHandlerNotVersionedHandler()
    {
        var response = await _client.PostAsJsonAsync(DraftPath, new
        {
            definitionId = "definition-1",
            snapshotId = "snapshot-1",
            state = EmptyState,
            artifactVersion = "draft"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The versioned handler would have bound versionId = "drafts"; only the draft handler matches here.
        Assert.Null(_starter.LastStart);
        var request = Assert.IsType<StartWorkflowDraftTestRun>(_starter.LastStartDraft);
        Assert.Equal("definition-1", request.DefinitionId);
        Assert.Equal("snapshot-1", request.SnapshotId);
    }

    [Fact]
    public async Task DraftRouteSelectionIsStableAcrossRepeatedRequests()
    {
        // The collision previously resolved nondeterministically between server runs; within a single
        // matcher it can still be confirmed stable across many requests.
        for (var i = 0; i < 20; i++)
        {
            _starter.LastStart = null;
            _starter.LastStartDraft = null;

            var response = await _client.PostAsJsonAsync(DraftPath, new
            {
                definitionId = "definition-1",
                snapshotId = $"snapshot-{i}",
                state = EmptyState,
                artifactVersion = "draft"
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(_starter.LastStart);
            Assert.IsType<StartWorkflowDraftTestRun>(_starter.LastStartDraft);
        }
    }

    [Fact]
    public async Task VersionedTestRunPathStillInvokesVersionedHandler()
    {
        var response = await _client.PostAsJsonAsync("/publishing/workflows/version-1/test-runs", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.IsType<StartWorkflowTestRun>(_starter.LastStart);
        Assert.Equal("version-1", request.VersionId);
    }

    private static object EmptyState => new
    {
        variables = Array.Empty<object>(),
        rootActivity = (object?)null,
        inputs = Array.Empty<object>(),
        outputs = Array.Empty<object>(),
        workflowActivityOptions = (object?)null,
        strategyOptions = (object?)null
    };

    private sealed class RecordingRequestSender : IRequestSender
    {
        public object? LastRequest { get; set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            LastRequest = request;

            var view = new WorkflowTestRunView(
                TestRunId: "testrun-1",
                DefinitionId: "definition-1",
                DefinitionVersionId: "draft:snapshot-1",
                ArtifactId: "test-artifact-1",
                WorkflowExecutionId: "exec-1",
                Status: "DispatchAccepted",
                CommandDispatchStatus: "Accepted",
                Reason: null,
                ExpiresAt: null);

            return Task.FromResult((T)(object)view);
        }
    }

}

[CollectionDefinition(nameof(WorkflowDraftTestRunRoutingTests), DisableParallelization = true)]
public sealed class WorkflowDraftTestRunRoutingTestCollection;
