using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// T071b — US1 scenario 2 over a real transport: an artifact that arrives only as a mounted closure envelope
/// answers a genuine inbound HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// T071 already proved import → activation → stimulus routing end to end, but through a test-owned stimulus
/// provider over a probe node. That was the right subject for that test; it leaves one question open, which is
/// whether a <em>real</em> transport's publish-time surface survives the trip through a portable envelope. This
/// answers it with nothing stubbed on the request path: a real ASP.NET Core pipeline, the production
/// <c>HttpEndpointMiddleware</c>, the real route table fed by the activation coordinator's observer notification,
/// the real stimulus router and start dispatcher.
/// </para>
/// <para>
/// Two things make it a genuinely different risk from T071. First, HTTP routing is <em>projected</em>: the route
/// table only learns the template because activation notifies an index observer, so an import that wrote bindings
/// without completing the activation sequence would 404 here while looking perfectly imported. Second, the node
/// must already carry its consumer key — the importer refuses to rewrite content-addressed bytes, so a descriptor
/// naming the CLR type would pass every gate and fault at first execution.
/// </para>
/// </remarks>
public sealed class ImportedHttpEndpointArtifactEndToEndTests : IAsyncLifetime
{
    private const string SourceId = "mounted-artifacts";
    private const string DefinitionId = "definition-imported-webhook";
    private const string Path = "imported/webhook";
    private const string ResultOutputName = "EndpointResult";
    private const string NodeId = "node-imported-endpoint";

    private readonly string _mount = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "elsa-http-import-e2e",
        Guid.NewGuid().ToString("N"));

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_mount);
        _fixture = await HttpEndpointHostFixture.StartWithArtifactReconciliationAsync(SourceId, _mount);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task An_imported_artifact_answers_a_real_inbound_request_and_the_run_observes_it()
    {
        var executable = MountWebhookArtifact();

        var entry = Assert.Single((await _fixture.ReconcileArtifactsAsync()).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.Equal(executable.Identity.ArtifactId, entry.ArtifactId);

        // The route table learned the template from the activation coordinator's observer notification — nothing
        // in this test told it about the endpoint.
        Assert.True(
            _fixture.RouteTableContains(Path),
            "The imported endpoint's route never reached the live route table — activation likely did not notify the trigger-index observer.");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/workflows/http/{Path}?tenant=acme")
        {
            Content = new StringContent("""{"orderId":7}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Elsa-Test", "header-value");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var workflowExecutionId = Assert.Single(await ReadStartedIdsAsync(response));

        // It ran the imported artifact, on the reference the importer minted for its activation — proving both
        // projections went live, not just the binding.
        var state = await _fixture.WorkflowExecutionAsync(workflowExecutionId);
        Assert.Equal(executable.Identity.ArtifactId, state.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowActivationReferenceIdentity.Create(entry.ActivationId!), state.PinnedSource!.SourceReferenceId);

        // And the run really observed the live request rather than merely being started by it.
        var captured = await _fixture.ReadResultProjectionAsync(workflowExecutionId, "Request", ResultOutputName);
        var model = HttpEndpointHostFixture.DeserializeRequest(captured);
        Assert.Equal("POST", model.Method);
        Assert.Equal(Path, model.Path);
        Assert.Equal("""{"orderId":7}""", model.Body);
        Assert.Equal("header-value", Assert.Contains("X-Elsa-Test", model.Headers)[0]);
        Assert.Equal("acme", Assert.Contains("tenant", model.Query)[0]);
    }

    [Fact]
    public async Task The_imported_binding_is_activation_scoped_and_shares_its_slot_with_the_minted_reference()
    {
        // The invariant that makes the request above resolvable at all: a binding's (ActivationId, SlotId) is what
        // WorkflowStartDispatcher selects the source reference by, so a binding whose activation minted no
        // matching reference routes to nothing.
        var executable = MountWebhookArtifact();

        var entry = Assert.Single((await _fixture.ReconcileArtifactsAsync()).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        var binding = Assert.Single(await _fixture.ListTriggerBindingsAsync(executable.Identity.ArtifactId));
        Assert.True(binding.IsActive);
        Assert.Equal(entry.ActivationId, binding.ActivationId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create(DefinitionId, WorkflowArtifactReconciler.DefaultSlotName), binding.SlotId);
        Assert.Equal(NodeId, binding.ExecutableNodeId);

        var reference = await _fixture.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(entry.ActivationId!));
        Assert.NotNull(reference);
        Assert.Equal(binding.SlotId, reference!.SlotId);
        Assert.Equal(binding.ActivationId, reference.ActivationId);
    }

    [Fact]
    public async Task A_request_that_does_not_match_the_imported_template_falls_through_to_the_sentinel()
    {
        // Guards the positive case against a route table that matches everything: the import adds exactly one
        // route, and everything outside the base path still passes through.
        MountWebhookArtifact();
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, Assert.Single((await _fixture.ReconcileArtifactsAsync()).Entries).Outcome);

        var unmatched = await _fixture.Client.PostAsync($"/workflows/http/some/other/path", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);

        var outsideBasePath = await _fixture.Client.GetAsync("/not-an-elsa-route");
        Assert.Equal(HttpEndpointHostFixture.SentinelStatusCode, (int)outsideBasePath.StatusCode);
    }

    private WorkflowExecutable MountWebhookArtifact()
    {
        var node = _fixture.NewImportableHttpEndpointTriggerNode(Path, ResultOutputName, ["POST"], NodeId);
        var executable = ArtifactClosureFixture.Executable(node, DefinitionId);
        ArtifactClosureFixture.Mount(_fixture.Services, _mount, "webhook.json", ArtifactClosureFixture.Closure(executable));
        return executable;
    }

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        return payload.RootElement.GetProperty("started").EnumerateArray().Select(element => element.GetString()!).ToArray();
    }
}
