using Elsa.Api.FastEndpoints.Abstractions;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Tests.TestSupport;

/// <summary>
/// A trivial secured endpoint built on the real <see cref="ElsaEndpointWithoutRequest{TResponse}"/>
/// base, so it is secured-by-construction: <see cref="ConfigurePermissions"/> always applies
/// <c>Permissions("*")</c>. Whether an unauthenticated caller receives 401 or 200 is therefore
/// decided solely by the shell's <c>ApiSecurity</c> composition, which is what the integration test
/// verifies.
/// </summary>
public sealed class PingEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "test/ping";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions();
    }

    public override Task HandleAsync(CancellationToken cancellationToken)
    {
        return Send.OkAsync("pong", cancellationToken);
    }
}
