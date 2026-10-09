using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Testing;
using Elsa.Secrets.Workflows.Tests.Support;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Elsa.Secrets.Workflows.Tests.Support.SecretsCanaryWorkflowHost;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// <c>SendHttpRequest</c> consumes a secret end to end (spec 188, slice 11, T108, FR-019, SC-001, A23), on the canary
/// host with the HTTP activities composed and the named client's primary handler replaced by the canary's local
/// endpoint. A workflow definition binds a stored secret to the activity's <c>Authorization</c> credential input and
/// publishes; the run sends the secret's value as the request's <c>Authorization</c> header and completes on <c>Done</c>;
/// after the secret is rotated, a second run of the same publication sends the new value; a literal on the input is
/// refused at draft save with <c>Inputs/CredentialLiteral</c>. Then, after each surface's precondition, the encoded
/// scanner finds neither header value on any of the eight surfaces. The endpoint answers without repeating a header, so
/// the scan covers the activity's own recording and not a server that reflects its input (spec Assumptions). Scope: the
/// <c>Authorization</c> input is named like a credential, so the name-based redactors (the snapshot factory's, the
/// OpenTelemetry bridge's) would hide it by name whatever the protections beneath do; the canary scenarios of
/// <see cref="WorkflowSecretCanaryTests"/> use names those redactors do not match, and this test proves the shipped
/// input's end-to-end behavior, not each protection alone.
/// </summary>
[Collection(CanaryHostCollection.Name)]
public sealed class SendHttpRequestSecretEndToEndTests(DiscreteHttpCanaryHostFixture fixture, ITestOutputHelper output)
    : IClassFixture<DiscreteHttpCanaryHostFixture>
{
    private const string Label = "http";
    private const string AuthorizationInput = nameof(SendHttpRequest.Authorization);
    private static readonly Uri Url = new("http://canary-endpoint.test/resource");

    private SecretsCanaryWorkflowHost Host => fixture.Host;

    [Theory]
    [MemberData(nameof(WorkflowSecretCanaryTests.Levels), MemberType = typeof(WorkflowSecretCanaryTests))]
    public async Task A_secret_bound_to_Authorization_is_sent_verbatim_rotates_without_republishing_and_is_found_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync(level);
        var rotated = NewHeaderValue();
        var publication = await Host.PublishActivityAsync(
            $"canary {scenario.Name}",
            scenario.NodeId,
            typeof(SendHttpRequest),
            Literal(nameof(SendHttpRequest.Url), Url.AbsoluteUri),
            Literal(nameof(SendHttpRequest.Method), "GET"),
            SecretReference(AuthorizationInput, scenario.ReferenceName));

        var received = Host.HttpEndpoint.Requests.Count;
        var first = await RunAsync(publication, scenario, scenario.Value);
        await Host.RotateSecretAsync(scenario.ReferenceName, rotated);
        var second = await RunAsync(publication, scenario, rotated);

        // The endpoint received each value verbatim as the one Authorization header value, and no other request.
        Assert.Equal(
            [("GET", Url, scenario.Value), ("GET", Url, rotated)],
            Host.HttpEndpoint.Requests.Skip(received).Select(request => (request.Method, request.Uri, Assert.Single(request.Authorization))));
        await new CanarySurfaces(Host).AssertAbsentAsync(
            scenario.Name,
            level,
            [Run(scenario, first, publication), Run(scenario, second, publication)],
            [scenario.Value, scenario.Token, rotated, TokenOf(rotated)],
            Enum.GetValues<CanarySurface>());
    }

    [Fact]
    public async Task A_literal_on_Authorization_is_refused_at_draft_save_and_is_stored_nowhere()
    {
        var scenario = await BeginAsync(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot);
        var literal = NewHeaderValue();
        var bound = await Host.StateOfAsync(
            scenario.NodeId,
            typeof(SendHttpRequest),
            Literal(nameof(SendHttpRequest.Url), Url.AbsoluteUri),
            SecretReference(AuthorizationInput, scenario.ReferenceName));
        var literalState = await Host.StateOfAsync(
            scenario.NodeId,
            typeof(SendHttpRequest),
            Literal(nameof(SendHttpRequest.Url), Url.AbsoluteUri),
            Literal(AuthorizationInput, literal));
        var added = await Host.PostAsync("design/workflows/definitions", new JsonObject { ["name"] = $"canary {scenario.Name}", ["initialState"] = bound });
        var draftId = Required(added, "draft", "id");

        using var refused = await Host.Send(HttpMethod.Put, $"design/workflows/drafts/{draftId}", CanaryCaller.Operator, new JsonObject { ["state"] = literalState });

        // The one error is keyed by the input's path and starts with the rule id; the literal is in neither the answer
        // nor any design database, which still holds the draft with the secret reference.
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.DoesNotContain(literal, body, StringComparison.Ordinal);
        var error = Assert.Single(JsonNode.Parse(body)!["errors"]!.AsObject());
        Assert.Equal($"{scenario.NodeId}/inputs/{AuthorizationInput}", error.Key);
        Assert.StartsWith("Inputs/CredentialLiteral", (string?)Assert.Single(error.Value!.AsArray()), StringComparison.Ordinal);
        var cells = await ReadDatabaseCellsAsync(Host.DesignDirectory);
        Assert.Contains(cells, cell => CanaryScanner.Find(cell.Bytes, scenario.ReferenceName, cell.Location).Count > 0);
        Assert.Empty(cells.SelectMany(cell => CanaryScanner.Find(cell.Bytes, literal, cell.Location)));
    }

    // ---- Scenario support ----------------------------------------------------------------------------------------

    private sealed record HttpScenario(string Name, RuntimeDiagnosticsEvidenceLevel Level, string NodeId, string ReferenceName, string Value)
    {
        /// <summary>The credential without the scheme the header value carries, which a scan searches for too.</summary>
        public string Token => TokenOf(Value);
    }

    /// <summary>Saves the diagnostics level, creates the scenario's secret and arms the endpoint to accept its value.</summary>
    private async Task<HttpScenario> BeginAsync(RuntimeDiagnosticsEvidenceLevel level)
    {
        var suffix = WorkflowSecretCanaryTests.Suffix();
        var scenario = new HttpScenario($"{Label}@{level}", level, $"{Label}-{suffix}", WorkflowSecretCanaryTests.ReferenceName(Label, suffix), NewHeaderValue());
        await Host.SaveDiagnosticsLevelAsync(level);
        await Host.CreateSecretAsync(scenario.ReferenceName, scenario.Value);
        output.WriteLine($"{scenario.Name}: node {scenario.NodeId}, reference {scenario.ReferenceName}");
        return scenario;
    }

    /// <summary>
    /// A header value as a bearer credential has it: a scheme and a token built from a Guid, a plain word of no
    /// credential's shape, generated per run.
    /// </summary>
    private static string NewHeaderValue() => $"Bearer canaryhttp{Guid.NewGuid():N}";

    private static string TokenOf(string headerValue) => headerValue["Bearer ".Length..];

    /// <summary>Runs the publication once, with the endpoint armed for <paramref name="expected"/>, and asserts it completed on <c>Done</c>.</summary>
    private async Task<string> RunAsync(CanaryPublication publication, HttpScenario scenario, string expected)
    {
        Host.HttpEndpoint.Expect(expected);
        var run = await Host.ExecuteAsync(publication);
        var node = await WaitForCompletionAsync(run, scenario.NodeId);
        Assert.Equal(["Done"], WorkflowExecutionRun.CompletionOutcomes(node));
        return run;
    }

    private static CanaryRun Run(HttpScenario scenario, string workflowExecutionId, CanaryPublication publication) =>
        new(
            scenario.Name,
            scenario.NodeId,
            workflowExecutionId,
            publication.ArtifactId,
            publication.SourceReferenceId,
            publication.DefinitionId,
            AuthorizationInput,
            scenario.ReferenceName,
            scenario.ReferenceName,
            ActivityTypePrefix: typeof(SendHttpRequest).FullName!,
            CompanionInput: nameof(SendHttpRequest.Method));

    /// <summary>
    /// Polls the run's committed state until the workflow completes, and returns the activity's state. A run that
    /// records an activity fault or an incident fails at once, naming it, rather than waiting out the timeout.
    /// </summary>
    private async Task<ActivityExecutionState> WaitForCompletionAsync(string workflowExecutionId, string nodeId)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var incidents = await Host.ReadIncidentsAsync(workflowExecutionId);
            var (activities, workflow) = await Host.ReadRunAsync(workflowExecutionId);
            var node = activities.SingleOrDefault(activity => activity.Execution.ExecutableNodeId == nodeId);
            if (workflow?.Status == WorkflowExecutionStatus.Completed && node is not null)
                return node;
            var faults = incidents.Select(incident => $"incident {incident.FailureType}")
                .Concat(activities.Where(activity => activity.Fault is not null).Select(activity => $"fault {activity.Fault!.Code} on node {activity.Execution.ExecutableNodeId}"))
                .ToArray();
            Assert.True(faults.Length == 0, $"Run '{workflowExecutionId}' faulted before it completed: {string.Join("; ", faults)}.");
            Assert.True(deadline.Elapsed < SettleTimeout, $"Run '{workflowExecutionId}' did not complete: workflow {workflow?.Status}.");
            await Task.Delay(100);
        }
    }

    /// <summary>Generous for a shared machine whose parallel sessions stretch every timing (AGENTS.md); a fault fails the wait at once.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);
}
