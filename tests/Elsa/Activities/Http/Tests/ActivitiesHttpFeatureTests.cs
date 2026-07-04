using CShells.Features;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesHttpFeature"/> (constitution §2.23.1). The HTTP
/// activities are constructed by the runtime's CLR activity constructor, so the feature registers no per-type
/// activity services; it does own the outbound transport, so the test pins the shell metadata and asserts the
/// named <see cref="System.Net.Http.IHttpClientFactory"/> client is configured.
/// </summary>
public sealed class ActivitiesHttpFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersHttpClientFactory_AndNoActivityConstructor()
    {
        var services = new ServiceCollection();

        new ActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IHttpClientFactory>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IActivityConstructor));
    }

    [Fact]
    public void ConfiguredClient_UsesTheWellKnownClientName()
    {
        var services = new ServiceCollection();
        new ActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpActivityConstants.HttpClientName);

        // The activity disables the ambient client timeout and enforces its own via a linked token source.
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void DeclaresExpectedShellFeatureMetadata()
    {
        var attribute = Assert.Single(
            typeof(ActivitiesHttpFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("ActivitiesHttp", attribute.Name);
    }
}
