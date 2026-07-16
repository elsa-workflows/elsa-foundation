using System.Net;
using System.Net.Http.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Verifies that the literal <c>workflows/drafts/test-runs</c> route is selected deterministically
/// over the parameterized <c>workflows/{versionId}/test-runs</c> route. Both endpoints are hosted
/// through FastEndpoints (exactly as in production) so the test exercises the real route matcher.
/// </summary>
[Collection(nameof(WorkflowDraftTestRunRoutingTests))]
public sealed class WorkflowDraftTestRunRoutingTests : IAsyncLifetime
{
    private const string DraftPath = "/publishing/workflows/drafts/test-runs";

    private readonly RecordingRequestSender _sender = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IRequestSender>(_sender);
        builder.Services.AddFastEndpoints(o =>
        {
            o.Assemblies = [typeof(StartWorkflowTestRun).Assembly];
            // This narrowly scoped route-selection host needs only the two test-run endpoints. Keeping discovery
            // explicit prevents unrelated management endpoints in the same feature assembly from pulling their
            // production services into a matcher-only test.
            o.Filter = type => type.Name is "Start" or "StartDraft" &&
                               type.Namespace == "Elsa.Workflows.Publishing.Api.Endpoints.TestRuns";
        });

        _app = builder.Build();
        // This test exercises route matching only; relax endpoint security the FastEndpoints way
        // instead of composing authentication.
        _app.UseFastEndpoints(c => c.Endpoints.Configurator = ep => ep.AllowAnonymous());
        await _app.StartAsync();

        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
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
        var request = Assert.IsType<StartWorkflowDraftTestRun>(_sender.LastRequest);
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
            _sender.LastRequest = null;

            var response = await _client.PostAsJsonAsync(DraftPath, new
            {
                definitionId = "definition-1",
                snapshotId = $"snapshot-{i}",
                state = EmptyState,
                artifactVersion = "draft"
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.IsType<StartWorkflowDraftTestRun>(_sender.LastRequest);
        }
    }

    [Fact]
    public async Task VersionedTestRunPathStillInvokesVersionedHandler()
    {
        var response = await _client.PostAsJsonAsync("/publishing/workflows/version-1/test-runs", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.IsType<StartWorkflowTestRun>(_sender.LastRequest);
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
