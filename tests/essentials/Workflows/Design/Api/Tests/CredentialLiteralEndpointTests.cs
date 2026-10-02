using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.CredentialLiteralTestSupport;
using AuthorizationHost = Elsa.Workflows.Design.Api.Tests.WorkflowsDesignApiContractTests.AuthorizationHost;

namespace Elsa.Workflows.Design.Api.Tests;

/// <summary>
/// Spec 188, FR-008 over HTTP, at every Design API route that writes workflow state (Definitions/Update has no route; it
/// is a mediator command, proven by direct call in <c>CredentialLiteralStorageTests</c>). A literal on a credential input
/// is answered with 400 and the contract's problem body: <c>errors</c> keyed by <c>{nodeId}/inputs/{referenceKey}</c>,
/// each message starting with the rule id, and the value nowhere; the route's command never runs. A secret reference and
/// a literal on a sensitive input that is not a credential reach the command. A promotion whose draft changed after
/// admission answers 409, and so does one the promotion command's in-lock validation gate refuses, with the gate's
/// errors keyed by path beside its general error.
/// </summary>
public sealed class CredentialLiteralEndpointTests
{
    public static TheoryData<string> Routes => new() { "DefinitionsAdd", "DraftsReplace", "VersionsAdd", "DefinitionsSubmit", "DraftsPromote" };

    [Theory]
    [MemberData(nameof(Routes))]
    public Task A_credential_literal_is_answered_with_400_keyed_by_the_input_path_and_no_command_runs(string route) =>
        AssertRefusedAsync(route, State(ActivityVersionId, Bind(CredentialKey, "Literal")));

    [Fact]
    public Task A_text_payload_under_the_secret_type_is_answered_with_400_and_no_command_runs() =>
        AssertRefusedAsync("DefinitionsAdd", State(ActivityVersionId, Bind(CredentialKey, "SecretText")));

    [Theory]
    [MemberData(nameof(Routes))]
    public Task A_secret_reference_on_a_credential_input_reaches_the_command(string route) =>
        AssertReachesTheCommandAsync(route, State(ActivityVersionId, Bind(CredentialKey, "Secret")));

    [Theory]
    [MemberData(nameof(Routes))]
    public Task A_literal_on_a_sensitive_input_that_is_not_a_credential_reaches_the_command(string route) =>
        AssertReachesTheCommandAsync(route, State(ActivityVersionId, Bind(SensitiveKey, "Literal")));

    [Fact]
    public async Task A_promotion_whose_draft_changed_after_admission_is_answered_with_409()
    {
        await using var host = await AuthorizationHost.StartAsync();
        host.Domain.PromoteFailure = new WorkflowDraftChangedException("route-draft");

        using var response = await SendAsync(host, "DraftsPromote", State(ActivityVersionId, Bind(CredentialKey, "Secret")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed after it was read for promotion", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_promotion_the_in_lock_validation_gate_refuses_is_answered_with_409_and_its_errors_by_path()
    {
        // The standard host's answer to a draft with an uncataloged node (contract, Known gaps): admission cannot judge
        // the node, the promotion command's in-lock gate reports it, and the design translator maps the gate's refusal.
        await using var host = await AuthorizationHost.StartAsync();
        var finding = new ValidationError(NodeId, "Graph/UnknownActivityVersion", $"Activity '{NodeId}' references an activity version the catalog does not hold.");
        var refusal = new DraftHasValidationErrorsException("route-draft", [finding]);
        host.Domain.PromoteFailure = refusal;

        using var response = await SendAsync(host, "DraftsPromote", State(ActivityVersionId, Bind(CredentialKey, "Secret")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal(2, errors.EnumerateObject().Count());
        Assert.Equal(finding.Message, Assert.Single(errors.GetProperty(finding.Path).EnumerateArray()).GetString());
        Assert.Equal(refusal.Message, Assert.Single(errors.GetProperty("generalErrors").EnumerateArray()).GetString());
    }

    /// <summary>
    /// Sends <paramref name="state"/> to <paramref name="route"/> and asserts the refusal's problem body: 400, one error
    /// keyed by the credential input's path whose message starts with the rule id, the bound value nowhere, no command run.
    /// </summary>
    private static async Task AssertRefusedAsync(string route, WorkflowDefinitionState state)
    {
        await using var host = await AuthorizationHost.StartAsync();

        using var response = await SendAsync(host, route, state);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Literal, body, StringComparison.Ordinal);
        var errors = JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateObject().ToArray();
        var error = Assert.Single(errors);
        Assert.Equal($"{NodeId}/inputs/{CredentialKey}", error.Name);
        Assert.StartsWith("Inputs/CredentialLiteral", Assert.Single(error.Value.EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.Empty(host.Domain.StateWrites);
    }

    private static async Task AssertReachesTheCommandAsync(string route, WorkflowDefinitionState state)
    {
        await using var host = await AuthorizationHost.StartAsync();

        using var response = await SendAsync(host, route, state);

        Assert.True(response.IsSuccessStatusCode, $"{route} answered {(int)response.StatusCode}.");
        Assert.Single(host.Domain.StateWrites);
    }

    /// <summary>
    /// Sends <paramref name="state"/> to <paramref name="route"/>. Each route names the state the draft store serves:
    /// promote carries no state, because it reads the draft, so its store serves <paramref name="state"/>; every other
    /// route's store serves the empty state it serves by default.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAsync(AuthorizationHost host, string route, WorkflowDefinitionState state)
    {
        var view = state.ToStateView();
        var empty = WorkflowDefinitionState.Empty;
        var (method, path, body, storedDraft) = route switch
        {
            "DefinitionsAdd" => (HttpMethod.Post, "/design/workflows/definitions", (object)new { name = "Definition", initialState = view }, empty),
            "DraftsReplace" => (HttpMethod.Put, "/design/workflows/drafts/route-draft", new { state = view }, empty),
            "VersionsAdd" => (HttpMethod.Post, "/design/workflows/versions/ingest", new { definitionId = "sample-definition", state = view }, empty),
            "DefinitionsSubmit" => (HttpMethod.Post, "/design/workflows/definitions/submit", new { name = "Submitted", state = view }, empty),
            "DraftsPromote" => (HttpMethod.Post, "/design/workflows/drafts/route-draft/promote", new { }, state),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };
        host.Domain.DraftState = storedDraft;

        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");
        return await host.Client.SendAsync(request);
    }
}
