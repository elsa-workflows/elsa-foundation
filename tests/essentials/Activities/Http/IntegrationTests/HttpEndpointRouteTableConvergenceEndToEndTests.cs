using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// #2190 end to end: two nodes over one durable database, each with its own host and so its own route table. The
/// observers that refresh a route table fire only on the node that made the change, so before this fix an endpoint
/// published on node A, or an HTTP bookmark created there, returned 404 on node B until B restarted. Each test first
/// shows B's 404 while its convergence pump is not running, so that the later success can only be the pump's doing,
/// then shows B serving the change within the bound.
/// </summary>
public sealed class HttpEndpointRouteTableConvergenceEndToEndTests : IAsyncLifetime
{
    private const string BasePath = "/workflows/http/";

    // The interval each node is configured with, and the bound a test waits for convergence: a generous multiple of
    // the interval, so a loaded machine does not turn a slow tick into a failure.
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    private readonly string _databaseDirectory = Path.Join(Path.GetTempPath(), $"elsa-http-two-nodes-{Guid.NewGuid():N}");
    private HttpEndpointHostFixture _nodeA = null!;
    private HttpEndpointHostFixture _nodeB = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_databaseDirectory);
        var databasePath = Path.Join(_databaseDirectory, "runtime.db");
        _nodeA = await HttpEndpointHostFixture.StartSharedDurableSqliteNodeAsync(databasePath, Interval);
        _nodeB = await HttpEndpointHostFixture.StartSharedDurableSqliteNodeAsync(databasePath, Interval);
    }

    public async Task DisposeAsync()
    {
        await _nodeB.DisposeAsync();
        await _nodeA.DisposeAsync();
        Directory.Delete(_databaseDirectory, recursive: true);
    }

    [Fact]
    public async Task An_endpoint_published_on_one_node_is_served_by_the_other_within_the_bound()
    {
        await _nodeA.PublishHttpEndpointWorkflowAsync("artifact-two-node-start", "two-node/orders", "request", "POST");
        Assert.True(_nodeA.RouteTableContains("two-node/orders"));

        await AssertStaysNotFoundWithoutConvergenceAsync(_nodeB, "two-node/orders", "two-node/orders");

        StartConvergenceOnBothNodes();
        var response = await PostUntilServedAsync(_nodeB, "two-node/orders");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await ReadIdsAsync(response, "started"));
        Assert.True(_nodeB.RouteTableContains("two-node/orders"));
    }

    [Fact]
    public async Task An_http_bookmark_created_on_one_node_is_resumed_through_the_other_within_the_bound()
    {
        await _nodeA.PublishResumableHttpEndpointWorkflowAsync("artifact-two-node-callback", "two-node/callbacks/{id}", "POST", "callback");
        var workflowExecutionId = await _nodeA.StartWorkflowDirectlyAsync("artifact-two-node-callback");
        Assert.True(_nodeA.RouteTableContains("two-node/callbacks/{id}"));

        await AssertStaysNotFoundWithoutConvergenceAsync(_nodeB, "two-node/callbacks/42", "two-node/callbacks/{id}");

        StartConvergenceOnBothNodes();
        var response = await PostUntilServedAsync(_nodeB, "two-node/callbacks/42");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(workflowExecutionId, Assert.Single(await ReadIdsAsync(response, "resumed")));
    }

    [Fact]
    public async Task An_endpoint_retired_on_one_node_leaves_the_other_nodes_route_table_within_the_bound()
    {
        StartConvergenceOnBothNodes();
        await _nodeA.PublishHttpEndpointWorkflowAsync("artifact-two-node-retired", "two-node/retired", "request", "POST");
        await WaitUntilAsync(() => _nodeB.RouteTableContains("two-node/retired"), "node B never routed the endpoint node A published");

        await _nodeA.RetireArtifactTriggerAsync("artifact-two-node-retired");

        await WaitUntilAsync(() => !_nodeB.RouteTableContains("two-node/retired"), "node B kept routing the endpoint node A retired");
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(_nodeB, "two-node/retired")).StatusCode);
    }

    private void StartConvergenceOnBothNodes()
    {
        _nodeA.StartRouteTableConvergence();
        _nodeB.StartRouteTableConvergence();
    }

    // Several intervals pass with nothing converging node B: the 404 is the defect, not a transient.
    private static async Task AssertStaysNotFoundWithoutConvergenceAsync(HttpEndpointHostFixture node, string path, string template)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(node, path)).StatusCode);
        await Task.Delay(Interval * 5);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(node, path)).StatusCode);
        Assert.False(node.RouteTableContains(template));
    }

    private static async Task<HttpResponseMessage> PostUntilServedAsync(HttpEndpointHostFixture node, string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var response = await PostAsync(node, path);
            if (response.StatusCode != HttpStatusCode.NotFound)
                return response;
            Assert.True(elapsed.Elapsed < Bound, $"'{path}' still returned 404 after {elapsed.Elapsed}; the bound is {Bound}.");
            await Task.Delay(Interval / 4);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < Bound, $"{failure} within {Bound}.");
            await Task.Delay(Interval / 4);
        }
    }

    private static Task<HttpResponseMessage> PostAsync(HttpEndpointHostFixture node, string path) =>
        node.Client.PostAsync($"{BasePath}{path}", new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"));

    private static async Task<IReadOnlyList<string>> ReadIdsAsync(HttpResponseMessage response, string property)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(property).EnumerateArray().Select(id => id.GetString()!).ToArray();
    }
}
