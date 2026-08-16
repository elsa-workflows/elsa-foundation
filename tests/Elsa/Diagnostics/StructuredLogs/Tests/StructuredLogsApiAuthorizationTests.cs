using System.Net;
using System.Reflection;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiAuthorizationTests
{
    public static IEnumerable<object[]> RecentAndSourcesAuthorizationCases() =>
    [
        ["anonymous", null!, HttpStatusCode.Unauthorized],
        ["missing", "missing", HttpStatusCode.Forbidden],
        ["adjacent", StructuredLogsApiHost.AdjacentIdentity, HttpStatusCode.Forbidden],
        ["exact", StructuredLogsApiHost.ExactIdentity, HttpStatusCode.OK],
        ["wildcard", StructuredLogsApiHost.WildcardIdentity, HttpStatusCode.OK],
        ["untrusted", StructuredLogsApiHost.UntrustedIdentity, HttpStatusCode.Unauthorized],
        ["ambiguous", "ambiguous", HttpStatusCode.Forbidden],
        ["resource-denied", StructuredLogsApiHost.ResourceDeniedIdentity, HttpStatusCode.Forbidden]
    ];

    [Theory]
    [MemberData(nameof(RecentAndSourcesAuthorizationCases))]
    public async Task Recent_and_sources_authorization_matrix_is_explicit_and_fail_closed(
        string _, string? identity, HttpStatusCode expected)
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();

        foreach (var path in new[]
                 {
                     StructuredLogsCompatibilityCases.RecentPath,
                     StructuredLogsCompatibilityCases.SourcesPath
                 })
        {
            using var response = await host.Client.SendAsync(Request(path, identity));
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("anonymous", null, 401)]
    [InlineData("missing", "missing", 403)]
    [InlineData("adjacent", "adjacent", 403)]
    [InlineData("untrusted", "untrusted", 401)]
    [InlineData("ambiguous", "ambiguous", 403)]
    [InlineData("resource-denied", "resource-denied", 403)]
    public async Task Rejected_stream_requests_never_start_an_sse_response_or_subscription(
        string _, string? identity, int expectedStatus)
    {
        StructuredLogsApiHost.ResetPermissionEvaluatorObservations();
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        var feed = host.Services.GetRequiredService<IStructuredLogLiveFeed>();
        var subscribersBefore = SubscriberCount(feed);

        using var response = await host.Client.SendAsync(
            Request(StructuredLogsCompatibilityCases.StreamPath, identity),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.NotEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(subscribersBefore, SubscriberCount(feed));
    }

    [Theory]
    [InlineData(StructuredLogsApiHost.ExactIdentity)]
    [InlineData(StructuredLogsApiHost.WildcardIdentity)]
    public async Task Exact_and_wildcard_principals_reach_both_read_routes(string identity)
    {
        StructuredLogsApiHost.ResetPermissionEvaluatorObservations();
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();

        foreach (var path in new[]
                 {
                     StructuredLogsCompatibilityCases.RecentPath,
                     StructuredLogsCompatibilityCases.SourcesPath
                 })
        {
            using var response = await host.Client.SendAsync(Request(path, identity));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.True(
            StructuredLogsApiHost.PermissionEvaluatorCallsFor(StructuredLogsCompatibilityCases.RecentPath) >= 1);
        Assert.True(
            StructuredLogsApiHost.PermissionEvaluatorCallsFor(StructuredLogsCompatibilityCases.SourcesPath) >= 1);
    }

    private static HttpRequestMessage Request(string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StructuredLogsApiHost.IdentityHeader, identity);
        return request;
    }

    private static int SubscriberCount(IStructuredLogLiveFeed feed)
    {
        var field = feed.GetType().GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(feed) is System.Collections.ICollection subscribers ? subscribers.Count : 0;
    }
}
