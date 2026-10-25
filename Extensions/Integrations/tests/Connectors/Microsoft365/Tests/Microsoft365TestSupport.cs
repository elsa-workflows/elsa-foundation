using System.Net.Http.Headers;
using System.Text.Json;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Microsoft365;
using Elsa.Integrations.Sdk.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.Microsoft365.Tests;

internal static class Microsoft365TestSupport
{
    public const string ConnectionName = "microsoft365";
    public const string Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.super-secret-access-token";
    public const string ClientSecret = "super-secret-client-secret-value";
    public const string GraphHost = "https://graph.microsoft.com/v1.0/";

    public static ConnectorHarness CreateHarness()
    {
        var harness = ConnectorHarness.Create(configure: services => services.AddMicrosoft365Connector());
        harness.Connections.Add(ConnectionName, "Bearer", Token);
        return harness;
    }

    public static T Activity<T>(ConnectorHarness harness, Action<T>? configure = null) where T : class
    {
        var activity = ActivatorUtilities.CreateInstance<T>(harness.Services);
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

    public static void AssertGraph(RecordedHttpRequest request, HttpMethod method, string relativePath)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal($"{GraphHost}{relativePath}", request.RequestUri?.ToString());
        AssertBearer(request);
    }

    public static string GraphError(string code, string message, string? extraJson = null)
    {
        var error = extraJson is null
            ? $"{{\"code\":\"{code}\",\"message\":\"{message}\"}}"
            : $"{{\"code\":\"{code}\",\"message\":\"{message}\",{extraJson}}}";
        return $"{{\"error\":{error}}}";
    }

    private static void SetConnection(object activity)
    {
        var property = activity.GetType().GetProperty(nameof(Microsoft365Activity<string>.Connection));
        property?.SetValue(activity, Reference());
    }
}
