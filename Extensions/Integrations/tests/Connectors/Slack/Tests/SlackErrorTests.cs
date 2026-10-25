using System.Net;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Integrations.Connectors.Slack;
using Elsa.Integrations.Connectors.Slack.Activities;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Slack.Tests;

public sealed class SlackErrorTests
{
    [Theory]
    [InlineData("channel_not_found")]
    [InlineData("not_in_channel")]
    [InlineData("cant_update_message")]
    public async Task Ok_false_faults_with_the_slack_error_code(string error)
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = $$"""{"ok":false,"error":"{{error}}"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
        });

        var fault = SlackTestSupport.Fault(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal(error, fault.Code);
        Assert.Contains(error, fault.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SlackTestSupport.Token, fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_scope_includes_needed_in_the_fault_message()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":false,"error":"missing_scope","needed":"chat:write"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
        });

        var fault = SlackTestSupport.Fault(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("missing_scope", fault.Code);
        Assert.Contains("chat:write", fault.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid_auth")]
    [InlineData("token_revoked")]
    [InlineData("account_inactive")]
    public async Task Auth_errors_throw_connection_unavailable(string error)
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = $$"""{"ok":false,"error":"{{error}}"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
        });

        var exception = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => activity.ExecuteForTestsAsync(SlackTestSupport.Context()).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, exception.Code);
        Assert.Equal(SlackTestSupport.ConnectionName, exception.ConnectionName);
        Assert.Contains(error, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SlackTestSupport.Token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warning_is_logged_and_does_not_fault()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"channel":"C1","ts":"1.2","warning":"missing_charset"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("C1", result.Channel);
        Assert.Contains(harness.LogMessages, message => message.Contains("missing_charset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rate_limit_retries_then_succeeds()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        var delays = new List<TimeSpan>();
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(
            HttpStatusCode.TooManyRequests,
            """{"ok":false,"error":"ratelimited"}""",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "0" }));
        harness.Http.EnqueueJson("""{"ok":true,"channel":"C1","ts":"1.2"}""");
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
            send.Retry = new SlackRetryOptions
            {
                MaxAttempts = 3,
                DelayAsync = (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }
            };
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("C1", result.Channel);
        Assert.Equal(2, harness.Http.Recorded.Count);
        Assert.Equal([TimeSpan.Zero], delays);
    }

    [Fact]
    public async Task Rate_limit_faults_when_the_retry_budget_is_exhausted()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        for (var i = 0; i < 3; i++)
        {
            harness.Http.Enqueue(CannedHttpResponse.WithStatus(
                HttpStatusCode.TooManyRequests,
                """{"ok":false,"error":"ratelimited"}""",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "2" }));
        }

        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
            send.Retry = new SlackRetryOptions
            {
                MaxAttempts = 3,
                DelayAsync = (_, _) => Task.CompletedTask
            };
        });

        var fault = SlackTestSupport.Fault(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("rate_limited", fault.Code);
        Assert.True(fault.IsRetryable);
        Assert.Equal(3, harness.Http.Recorded.Count);
    }

    [Fact]
    public async Task Rate_limit_faults_when_retry_after_would_exceed_the_total_cap()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.Enqueue(CannedHttpResponse.WithStatus(
            HttpStatusCode.TooManyRequests,
            "{}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "120" }));
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
            send.Retry = new SlackRetryOptions
            {
                MaxAttempts = 3,
                MaxRetryAfter = TimeSpan.FromSeconds(30),
                MaxTotalWait = TimeSpan.FromSeconds(20),
                DelayAsync = (_, _) => Task.CompletedTask
            };
        });

        var fault = SlackTestSupport.Fault(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("rate_limited", fault.Code);
        Assert.Single(harness.Http.Recorded);
    }
}
