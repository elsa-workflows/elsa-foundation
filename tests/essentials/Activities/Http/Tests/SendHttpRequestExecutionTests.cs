using System.Net;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Primitives;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage for the <c>SendHttpRequest</c> activity running through the real workflow
/// agent on the shared <see cref="WorkflowExecutionHarness"/>. The activity is constructed by the production
/// <see cref="Elsa.Activities.Primitives.Constructors.ClrActivityConstructor"/> (registered by
/// <see cref="ActivitiesPrimitivesFeature"/>), and the outbound transport is stubbed by overriding the named
/// client's primary handler — so the tests exercise the descriptor → construct → bind → execute path and the
/// activity's outcome mapping without a live network.
/// </summary>
public sealed class SendHttpRequestExecutionTests
{
    [Fact]
    public async Task SuccessfulResponse_WithoutExpectedCodes_EmitsDoneOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Response_MatchingExpectedCode_BranchesOnStatusCodeOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.Created, "made"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewSendNode(expectedStatusCodes: new[] { 201, 202 })));

        // Issue #926: a matching status branches on the outcome port named after its numeric code.
        run.AssertOutcomes(NodeId, "201");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Response_NotMatchingExpectedCode_BranchesOnUnmatchedStatusCodeOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "ok"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewSendNode(expectedStatusCodes: new[] { 201 })));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.UnmatchedStatusCode);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task ErrorStatus_WithoutExpectedCodes_EmitsFailedOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.InternalServerError, "boom"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Failed);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task TransportFailure_EmitsFailedOutcome()
    {
        await using var harness = NewHarness(_ => throw new HttpRequestException("no route to host"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Failed);
        run.AssertWorkflowCompleted();
    }

    // Spec 188, T105 (research R17, FR-019). Values are built at run time from a Guid so no credential-shaped
    // literal sits in source; the stub answers without repeating any request header.
    private readonly string _authorizationValue = $"Bearer canary-{Guid.NewGuid():N}";
    private readonly string _headersEntryValue = $"Basic canary-{Guid.NewGuid():N}";
    private readonly List<IReadOnlyDictionary<string, string[]>> _requests = [];

    [Fact]
    public async Task AuthorizationInput_IsSentVerbatimAsTheRequestsSingleAuthorizationHeaderValue()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(authorization: _authorizationValue)));

        run.AssertOutcomes(NodeId, ActivityOutcomes.Done);
        Assert.Equal([_authorizationValue], Assert.Single(AuthorizationHeaders(Assert.Single(_requests))));
    }

    [Fact]
    public async Task AuthorizationInput_ReplacesARequestHeadersEntry_WhateverItsLetterCase()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(
            authorization: _authorizationValue,
            requestHeaders: new Dictionary<string, string> { ["authorization"] = _headersEntryValue, ["X-Trace"] = "kept" })));

        run.AssertOutcomes(NodeId, ActivityOutcomes.Done);
        var request = Assert.Single(_requests);
        Assert.Equal([_authorizationValue], Assert.Single(AuthorizationHeaders(request)));
        Assert.Equal(["kept"], request["X-Trace"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UnboundOrEmptyAuthorization_LeavesTheRequestHeadersEntryUnchanged(string? authorization)
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(
            authorization: authorization,
            requestHeaders: new Dictionary<string, string> { ["authorization"] = _headersEntryValue })));

        run.AssertOutcomes(NodeId, ActivityOutcomes.Done);
        Assert.Equal([_headersEntryValue], Assert.Single(AuthorizationHeaders(Assert.Single(_requests))));
    }

    [Fact]
    public async Task Result_HoldsOnlyTheResponse_AndNeitherAuthorizationValue()
    {
        await using var harness = NewHarness(_ =>
        {
            var response = Respond(HttpStatusCode.OK, "stub body");
            response.Headers.Add("X-Stub", "answered");
            return response;
        });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(
            authorization: _authorizationValue,
            requestHeaders: new Dictionary<string, string> { ["authorization"] = _headersEntryValue, ["X-Trace"] = "sent" })));

        var result = run.AssertCompleted(NodeId).Completion!.Result.InlineValue!.Value;
        var serialized = result.GetRawText();
        Assert.DoesNotContain(_authorizationValue, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(_headersEntryValue, serialized, StringComparison.Ordinal);
        Assert.Equal("stub body", result.GetProperty("responseBody").GetString());
        // The stub's own headers only: its X-Stub header and the Content-Length and Content-Type its body carries, no request header.
        var responseHeaders = result.GetProperty("responseHeaders").EnumerateObject().Select(header => header.Name).Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["Content-Length", "Content-Type", "X-Stub"], responseHeaders);
    }

    private static string[][] AuthorizationHeaders(IReadOnlyDictionary<string, string[]> request) =>
        request.Where(header => string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value)
            .ToArray();

    private WorkflowExecutionHarness NewHarness(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        SendHttpRequestTestSupport.NewStubbedHarness(request =>
        {
            lock (_requests)
                _requests.Add(request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
            return responder(request);
        });
}
