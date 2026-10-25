using System.Net;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365;
using Elsa.Integrations.Connectors.Microsoft365.Activities;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Microsoft365.Tests;

public sealed class Microsoft365ErrorTests
{
    [Fact]
    public async Task Graph_error_faults_with_code_and_message_only()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = HttpStatusCode.BadRequest;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError(
            "ErrorInvalidMailboxItemId",
            "The item id is invalid.",
            extraJson: "\"innerError\":{\"request-id\":\"abc\",\"access_token\":\"" + Microsoft365TestSupport.Token + "\",\"client_secret\":\"" + Microsoft365TestSupport.ClientSecret + "\"}");
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("ErrorInvalidMailboxItemId", fault.Code);
        Assert.Contains("The item id is invalid.", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("innerError", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("request-id", fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.ClientSecret, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, fault.Code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization_RequestDenied")]
    [InlineData("ErrorAccessDenied")]
    public async Task Forbidden_names_the_required_scope(string code)
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = HttpStatusCode.Forbidden;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError(code, "Access denied");
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal(code, fault.Code);
        Assert.Contains("Mail.Send", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidAuthenticationToken_throws_connection_unavailable()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = HttpStatusCode.Unauthorized;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError(
            "InvalidAuthenticationToken",
            "Access token has expired.",
            extraJson: "\"innerError\":{\"access_token\":\"" + Microsoft365TestSupport.Token + "\"}");
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var exception = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, exception.Code);
        Assert.Equal(Microsoft365TestSupport.ConnectionName, exception.ConnectionName);
        Assert.Contains("InvalidAuthenticationToken", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_grant_throws_connection_unavailable()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = HttpStatusCode.BadRequest;
        harness.Http.ResponseContent = """{"error":"invalid_grant","error_description":"AADSTS70000: The refresh token is invalid."}""";
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var exception = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, exception.Code);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_retries_then_succeeds()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var delays = new List<TimeSpan>();
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(
            HttpStatusCode.TooManyRequests,
            Microsoft365TestSupport.GraphError("TooManyRequests", "throttled"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "0" }));
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(HttpStatusCode.Accepted, ""));
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.Retry = new Microsoft365RetryOptions
            {
                MaxAttempts = 3,
                DelayAsync = (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }
            };
        });

        var transition = await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context());
        Assert.Equal(Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Complete, transition.Kind);
        Assert.Equal(2, harness.Http.Recorded.Count);
        Assert.Equal([TimeSpan.Zero], delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Throttled_5xx_retries_then_succeeds(HttpStatusCode status)
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(
            status,
            Microsoft365TestSupport.GraphError("UnknownError", "try again"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "0" }));
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(HttpStatusCode.Accepted, ""));
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.Retry = new Microsoft365RetryOptions
            {
                MaxAttempts = 3,
                DelayAsync = (_, _) => Task.CompletedTask
            };
        });

        var transition = await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context());
        Assert.Equal(Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Complete, transition.Kind);
        Assert.Equal(2, harness.Http.Recorded.Count);
    }

    [Fact]
    public async Task Rate_limit_faults_when_the_retry_budget_is_exhausted()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        for (var i = 0; i < 3; i++)
        {
            harness.Http.Enqueue(CannedHttpResponse.WithStatus(
                HttpStatusCode.TooManyRequests,
                Microsoft365TestSupport.GraphError("TooManyRequests", "throttled"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "2" }));
        }

        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.Retry = new Microsoft365RetryOptions
            {
                MaxAttempts = 3,
                DelayAsync = (_, _) => Task.CompletedTask
            };
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("rate_limited", fault.Code);
        Assert.True(fault.IsRetryable);
        Assert.Equal(3, harness.Http.Recorded.Count);
    }

    [Fact]
    public async Task Rate_limit_faults_when_retry_after_would_exceed_the_total_cap()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(
            HttpStatusCode.TooManyRequests,
            "{}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "120" }));
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.Retry = new Microsoft365RetryOptions
            {
                MaxAttempts = 3,
                MaxRetryAfter = TimeSpan.FromSeconds(30),
                MaxTotalWait = TimeSpan.FromSeconds(20),
                DelayAsync = (_, _) => Task.CompletedTask
            };
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("rate_limited", fault.Code);
        Assert.Single(harness.Http.Recorded);
    }

    [Fact]
    public async Task Http_5xx_is_a_retryable_fault()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = HttpStatusCode.BadGateway;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError("UnknownError", "unavailable");
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("UnknownError", fault.Code);
        Assert.True(fault.IsRetryable);
        Assert.DoesNotContain(Microsoft365TestSupport.Token, fault.Message, StringComparison.Ordinal);
        Assert.Single(harness.Http.Recorded);
    }
}
