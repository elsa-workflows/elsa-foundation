using System.Net;
using CShells.Features;
using Elsa.Activities.Http.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>One request the canary's HTTP endpoint received: its method, address and every <c>Authorization</c> header value.</summary>
public sealed record CanaryHttpRequest(string Method, Uri Uri, IReadOnlyList<string> Authorization);

/// <summary>
/// The canary's local HTTP endpoint (spec 188, T108, research R17): the transport behind the <c>SendHttpRequest</c>
/// activity's named client, in place of the network. It answers 200 when the request carries exactly one
/// <c>Authorization</c> header value and it equals the value the scenario expects, and 401 otherwise, and its answer
/// holds no header and a fixed body, so it never repeats a value it received. A server that reflected the value is the
/// case the spec's Assumptions leave out. Registered in the canary host's root services, where both the shell and the
/// tests see it.
/// </summary>
public sealed class CanaryHttpEndpoint
{
    private readonly List<CanaryHttpRequest> _requests = [];
    private string? _expected;

    /// <summary>Every request received, in the order received.</summary>
    public IReadOnlyList<CanaryHttpRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    /// <summary>From now on, the endpoint answers 200 to a request whose one <c>Authorization</c> value is <paramref name="authorization"/>.</summary>
    public void Expect(string authorization)
    {
        lock (_requests)
            _expected = authorization;
    }

    internal HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.ToArray() : [];
        bool accepted;
        lock (_requests)
        {
            _requests.Add(new CanaryHttpRequest(request.Method.Method, request.RequestUri!, authorization));
            accepted = _expected is not null && authorization.Length == 1 && authorization[0] == _expected;
        }

        return new HttpResponseMessage(accepted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(accepted ? "accepted" : "refused")
        };
    }
}

/// <summary>The primary handler of the named client in a canary host that composes the HTTP activities.</summary>
internal sealed class CanaryHttpEndpointHandler(CanaryHttpEndpoint endpoint) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(endpoint.Answer(request));
}

/// <summary>
/// Composes the canary's HTTP endpoint into the canary host's shell, and nowhere else (spec 188, T108): the named client
/// <c>ActivitiesHttpFeature</c> configures gets the endpoint as its primary handler, a replacement made after every
/// feature has registered its own, so the client's real pipeline (its logging handlers included) is the one in front of
/// it. Enabled only in a host started with the HTTP activities.
/// </summary>
[ShellFeature(
    name: Name,
    DisplayName = "Secrets canary HTTP transport",
    Description = "Test-only local endpoint behind the HTTP activities' named client in the spec 188 canary.",
    DependsOn = new object[] { "ActivitiesHttp" })]
public sealed class SecretsCanaryHttpTransportFeature : IShellFeature, IPostConfigureShellServices
{
    public const string Name = "SecretsCanaryHttpTransport";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void PostConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider => new CanaryHttpEndpointHandler(provider.GetRequiredService<CanaryHttpEndpoint>()));
}
