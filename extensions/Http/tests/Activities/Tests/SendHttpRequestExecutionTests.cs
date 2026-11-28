using System.Net;
using System.Text.Json;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

/// <summary>Verifies the outbound activity's outcomes and result using the production named client.</summary>
public sealed class SendHttpRequestExecutionTests
{
    // Values are built at run time from a Guid so no credential-shaped
    // literal sits in source; the stub answers without repeating any request header.
    private readonly string _authorizationValue = NewHeaderValue();
    private readonly string _headersEntryValue = NewHeaderValue("Basic");
    private readonly List<IReadOnlyDictionary<string, string[]>> _requests = [];

    [Fact]
    public async Task SuccessfulResponse_WithoutExpectedCodes_EmitsDoneOutcome()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await SendAsync(host);

        Assert.Equal(ActivityOutcomes.Done, run.Outcome);
    }

    [Fact]
    public async Task Response_MatchingExpectedCode_BranchesOnStatusCodeOutcome()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.Created, "made"));

        var run = await SendAsync(host, expectedStatusCodes: [201, 202]);

        // Issue #926: a matching status branches on the outcome port named after its numeric code.
        Assert.Equal("201", run.Outcome);
    }

    [Fact]
    public async Task Response_NotMatchingExpectedCode_BranchesOnUnmatchedStatusCodeOutcome()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.OK, "ok"));

        var run = await SendAsync(host, expectedStatusCodes: [201]);

        Assert.Equal(HttpActivityOutcomes.UnmatchedStatusCode, run.Outcome);
    }

    [Fact]
    public async Task ErrorStatus_WithoutExpectedCodes_EmitsFailedOutcome()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.InternalServerError, "boom"));

        var run = await SendAsync(host);

        Assert.Equal(HttpActivityOutcomes.Failed, run.Outcome);
    }

    [Fact]
    public async Task TransportFailure_EmitsFailedOutcome()
    {
        await using var host = NewHost(_ => throw new HttpRequestException("no route to host"));

        var run = await SendAsync(host);

        Assert.Equal(HttpActivityOutcomes.Failed, run.Outcome);
    }

    [Fact]
    public async Task UnboundConnection_LeavesTheRequestHeadersEntryUnchanged()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await SendAsync(host, requestHeaders: new Dictionary<string, string> { ["authorization"] = _headersEntryValue });

        Assert.Equal(ActivityOutcomes.Done, run.Outcome);
        Assert.Equal([_headersEntryValue], Assert.Single(AuthorizationHeaders(Assert.Single(_requests))));
    }

    [Fact]
    public async Task BoundConnection_WithoutConnectionsWorkflows_FaultsBeforeSend()
    {
        await using var host = NewHost(_ => Respond(HttpStatusCode.OK, "hello"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SendAsync(host, connection: new ConnectionReference("acme-api", "http")));

        Assert.Contains("acme-api", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Result_HoldsOnlyTheResponse_AndNotRequestHeaderValues()
    {
        await using var host = NewHost(_ =>
        {
            var response = Respond(HttpStatusCode.OK, "stub body");
            response.Headers.Add("X-Stub", "answered");
            return response;
        });

        var completion = await SendAsync(host, requestHeaders:
            new Dictionary<string, string> { ["authorization"] = _headersEntryValue, ["X-Trace"] = "sent" });

        var result = JsonSerializer.SerializeToElement(completion.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

    private ServiceProvider NewHost(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        NewServices(StubTransport(request =>
        {
            lock (_requests)
                _requests.Add(request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
            return responder(request);
        }));
}
