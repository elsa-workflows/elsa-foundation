using System.Net.Http.Headers;
using System.Text.Json;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack;
using Elsa.Integrations.Sdk.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.Slack.Tests;

internal static class SlackTestSupport
{
    public const string ConnectionName = "slack";
    public const string Token = "xoxb-super-secret-token-value";

    public static ConnectorHarness CreateHarness()
    {
        var harness = ConnectorHarness.Create(configure: services => services.AddSlackConnector());
        harness.Connections.Add(ConnectionName, "Bearer", Token);
        return harness;
    }

    public static T Activity<T>(ConnectorHarness harness, Action<T>? configure = null) where T : class
    {
        var activity = ActivatorUtilities.CreateInstance<T>(harness.Services);
        if (activity is SlackActivity<ActivityUnit> unit)
            unit.Connection = Reference();
        else
            SetConnection(activity);
        configure?.Invoke(activity);
        return activity;
    }

    public static ConnectionReference Reference() => new(ConnectionName, "http");

    public static ActivityExecutionContext Context() =>
        new("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None);

    public static TResult Complete<TResult>(ActivityTransition<TResult> transition)
    {
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<TResult>>(transition);
        return completion.Result;
    }

    public static ActivityFault Fault<TResult>(ActivityTransition<TResult> transition)
    {
        var faulted = Assert.IsAssignableFrom<IActivityFaultTransition>(transition);
        return faulted.Fault;
    }

    public static JsonElement BodyJson(RecordedHttpRequest request)
    {
        Assert.False(string.IsNullOrWhiteSpace(request.Body));
        return JsonDocument.Parse(request.Body!).RootElement.Clone();
    }

    public static void AssertBearer(RecordedHttpRequest request)
    {
        Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), request.Authorization);
    }

    public static void AssertSlackMethod(RecordedHttpRequest request, string method)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://slack.com/api/{method}", request.RequestUri?.ToString());
        AssertBearer(request);
    }

    private static void SetConnection(object activity)
    {
        var property = activity.GetType().GetProperty(nameof(SlackActivity<string>.Connection));
        property?.SetValue(activity, Reference());
    }
}
