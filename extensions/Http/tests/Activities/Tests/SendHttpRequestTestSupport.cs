using System.Net;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>Builds the production named client and invokes the HTTP activity with explicit inputs.</summary>
internal static class SendHttpRequestTestSupport
{
    /// <summary>
    /// The category the named client's logger writes under (the default client handler's category), written out so a
    /// change to the production constant turns the tests red.
    /// </summary>
    public const string ClientLoggerCategory = "System.Net.Http.HttpClient.Elsa.Activities.Http.ClientHandler";

    /// <summary>
    /// A header value built at run time from a Guid (a scheme, a space and a plain word), so no credential-shaped literal
    /// sits in source and every call is distinct.
    /// </summary>
    public static string NewHeaderValue(string scheme = "Bearer") => $"{scheme} canary-{Guid.NewGuid():N}";

    public static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    public static ServiceProvider NewServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new KernelActivitiesHttpFeature().ConfigureServices(services);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static Action<IServiceCollection> StubTransport(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        services => services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responder));

    public static async Task<IActivityCompletionTransition<SendHttpRequestResult>> SendAsync(IServiceProvider services,
        Uri? url = null, int[]? expectedStatusCodes = null, ConnectionReference? connection = null, IDictionary<string, string>? requestHeaders = null)
    {
        using var scope = services.CreateScope();
        var activity = ActivatorUtilities.CreateInstance<SendHttpRequest>(scope.ServiceProvider);
        activity.Url = url ?? new Uri("https://example.test/resource");
        activity.ExpectedStatusCodes = expectedStatusCodes;
        activity.Connection = connection;
        activity.RequestHeaders = requestHeaders;
        var transition = await ((IActivity)activity).ExecuteAsync(
            new("wfexec-http", "actexec-http", "attempt-http", "node-send-http", CancellationToken.None));
        return Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
