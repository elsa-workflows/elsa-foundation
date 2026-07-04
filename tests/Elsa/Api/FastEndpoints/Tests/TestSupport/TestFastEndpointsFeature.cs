using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Api.FastEndpoints.Tests.TestSupport;

/// <summary>
/// A minimal Elsa FastEndpoints shell feature used by the integration tests. Its assembly (this test
/// assembly) is what the CShells <c>FastEndpoints</c> feature scans for endpoint definitions, so the
/// <see cref="PingEndpoint"/> and <see cref="NotFoundEndpoint"/> below are discovered and mapped for any
/// shell that enables this feature. Deriving <see cref="FastEndpointsFeatureBase"/> also registers the real
/// <c>ApiSecurityFastEndpointsConfigurator</c> for the shell — exactly as production features do.
/// </summary>
[ShellFeature(
    name: "TestEndpoints",
    DisplayName = "Test Endpoints",
    Description = "Hosts a single secured ping endpoint for per-shell security integration testing."
)]
public sealed class TestFastEndpointsFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // NotFoundEndpoint is discovered in this assembly and mapped by every shell that mounts this feature,
        // so its IRequestSender dependency must be resolvable at endpoint-registration time in all of them
        // (otherwise FastEndpoints aborts registration for the shell). Only the error-contract test invokes it;
        // the security shells never hit the route, so the always-throwing stub is harmless there.
        services.TryAddScoped<IRequestSender, EntityNotFoundRequestSender>();
    }
}


