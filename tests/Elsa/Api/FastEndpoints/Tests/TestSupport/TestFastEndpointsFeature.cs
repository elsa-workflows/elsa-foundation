using CShells.Features;
using Elsa.Api.FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.FastEndpoints.Tests.TestSupport;

/// <summary>
/// A minimal Elsa FastEndpoints shell feature used by the integration test. Its assembly (this test
/// assembly) is what the CShells <c>FastEndpoints</c> feature scans for endpoint definitions, so the
/// <see cref="PingEndpoint"/> below is discovered and mapped for any shell that enables this feature.
/// Deriving <see cref="FastEndpointsFeatureBase"/> also registers the real
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
    }
}
