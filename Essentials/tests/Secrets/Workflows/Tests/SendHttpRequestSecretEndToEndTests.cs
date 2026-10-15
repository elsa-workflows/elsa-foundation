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
/// <c>SendHttpRequest</c> consumes an http connection whose bearer token is a stored secret. A workflow
/// binds the connection by name; the run sends the secret as the request's <c>Authorization</c> header;
/// after the secret is rotated, a second run of the same publication sends the new value. A string
/// literal on the connection input is refused at publish. The encoded scanner finds neither header
/// value on the canary surfaces.
/// </summary>
[Collection(CanaryHostCollection.Name)]
public sealed class SendHttpRequestSecretEndToEndTests(DiscreteHttpCanaryHostFixture fixture, ITestOutputHelper output)
    : IClassFixture<DiscreteHttpCanaryHostFixture>
{
    private const string Label = "http";
    private const string ConnectionInput = nameof(SendHttpRequest.Connection);
    private static readonly Uri Url = new("http://canary-endpoint.test/resource");

    private SecretsCanaryWorkflowHost Host => fixture.Host;

    [Theory]
    [MemberData(nameof(WorkflowSecretCanaryTests.Levels), MemberType = typeof(WorkflowSecretCanaryTests))]
    public async Task A_secret_behind_an_http_connection_is_sent_as_Authorization_rotates_without_republishing_and_is_found_on_no_surface(RuntimeDiagnosticsEvidenceLevel level)
    {
        var scenario = await BeginAsync(level);
        var rotated = NewHeaderValue();
        var publication = await Host.PublishActivityAsync(
            $"canary {scenario.Name}",
            scenario.NodeId,
            typeof(SendHttpRequest),
            Literal(nameof(SendHttpRequest.Url), Url.AbsoluteUri),
            Literal(nameof(SendHttpRequest.Method), "GET"),
            ConnectionReference(ConnectionInput, scenario.ConnectionName));

        var received = Host.HttpEndpoint.Requests.Count;
        var first = await RunAsync(publication, scenario, scenario.Value);
        await Host.RotateSecretAsync(scenario.ReferenceName, rotated);
        var second = await RunAsync(publication, scenario, rotated);

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
    public async Task A_literal_token_on_Connection_is_refused_at_publish_and_is_stored_nowhere()
    {
        var scenario = await BeginAsync(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot);
        var literal = NewHeaderValue();
        var literalState = await Host.StateOfAsync(
            scenario.NodeId,
            typeof(SendHttpRequest),
            Literal(nameof(SendHttpRequest.Url), Url.AbsoluteUri),
            Literal(ConnectionInput, literal));

        using var submitted = await Host.Send(
            HttpMethod.Post,
            "design/workflows/definitions/submit",
            CanaryCaller.Operator,
            new JsonObject
            {
                ["name"] = $"canary {scenario.Name}",
                ["description"] = "literal connection must not be stored",
                ["state"] = literalState
            });
        var body = await submitted.Content.ReadAsStringAsync();
        Assert.True(submitted.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
        Assert.DoesNotContain(literal, body, StringComparison.Ordinal);
        var cells = await ReadDatabaseCellsAsync(Host.DesignDirectory);
        Assert.Empty(cells.SelectMany(cell => CanaryScanner.Find(cell.Bytes, literal, cell.Location)));
    }

    private sealed record HttpScenario(string Name, RuntimeDiagnosticsEvidenceLevel Level, string NodeId, string ReferenceName, string ConnectionName, string Value)
    {
        public string Token => TokenOf(Value);
    }

    private async Task<HttpScenario> BeginAsync(RuntimeDiagnosticsEvidenceLevel level)
    {
        var suffix = WorkflowSecretCanaryTests.Suffix();
        var scenario = new HttpScenario(
            $"{Label}@{level}",
            level,
            $"{Label}-{suffix}",
            WorkflowSecretCanaryTests.ReferenceName(Label, suffix),
            $"http-{suffix}",
            NewHeaderValue());
        await Host.SaveDiagnosticsLevelAsync(level);
        await Host.CreateSecretAsync(scenario.ReferenceName, scenario.Value);
        await Host.CreateHttpBearerConnectionAsync(scenario.ConnectionName, scenario.ReferenceName);
        output.WriteLine($"{scenario.Name}: node {scenario.NodeId}, connection {scenario.ConnectionName}, reference {scenario.ReferenceName}");
        return scenario;
    }

    private static string NewHeaderValue() => $"Bearer canaryhttp{Guid.NewGuid():N}";

    private static string TokenOf(string headerValue) => headerValue["Bearer ".Length..];

    private async Task<string> RunAsync(CanaryPublication publication, HttpScenario scenario, string expected)
    {
        Host.HttpEndpoint.Expect(expected);
        var run = await Host.ExecuteAsync(publication);
        var state = await Host.WaitForAsync(run, settled => Completed(settled) && settled.Node(scenario.NodeId) is not null);
        Assert.Equal(["Done"], WorkflowExecutionRun.CompletionOutcomes(state.Node(scenario.NodeId)!));
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
            ConnectionInput,
            ReferenceName: null,
            RuntimeMarker: scenario.ConnectionName,
            SubjectWithheld: false,
            ActivityTypePrefix: typeof(SendHttpRequest).FullName!,
            CompanionInput: nameof(SendHttpRequest.Method));
}
