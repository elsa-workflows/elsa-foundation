using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseTransportCompositionTests
{
    [Fact]
    public void Explicit_codec_override_is_preserved_and_can_parse_through_the_public_port()
    {
        var services = new ServiceCollection();
        var selected = new ReleaseBundleCodec();
        services.AddScoped<IReleaseBundleCodec>(_ => selected);
        var feature = new WorkflowReleaseTransportFeature();
        feature.ConfigureServices(services);
        feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var codec = scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>();
        Assert.Same(selected, codec);
        var fixture = new ReleaseBundleFixture();
        Assert.Equal(fixture.Validate().Digest, codec.Validate(fixture.Bytes, fixture.Blobs).Digest);
    }

    [Fact]
    public void A_later_duplicate_canonical_registration_refuses_shell_composition()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowReleaseTransportFeature();
        feature.ConfigureServices(services);
        services.AddScoped<IReleaseBundleCodec, ReleaseBundleCodec>();
        Assert.Contains("Exactly one", Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Keyed_private_codecs_do_not_compete_with_the_canonical_scoped_codec()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowReleaseTransportFeature();
        feature.ConfigureServices(services);
        services.AddKeyedScoped<IReleaseBundleCodec, ReleaseBundleCodec>("private");
        feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), second.ServiceProvider.GetRequiredService<IReleaseBundleCodec>());
    }
}
