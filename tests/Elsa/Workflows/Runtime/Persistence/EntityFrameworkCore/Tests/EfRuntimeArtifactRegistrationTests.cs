using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeArtifactRegistrationTests
{
    private const string SigningKey = "ef-runtime-registration-signing-key-32-bytes";

    [Fact]
    public void Feature_exposes_the_durable_signing_key_and_shares_it_across_scopes_and_nodes()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var feature = new RuntimeArtifactsEntityFrameworkCoreFeature
        {
            RecoveryContinuationSigningKey = SigningKey
        };
        feature.ConfigureServices(services);

        Assert.Contains(
            typeof(RuntimeArtifactsEntityFrameworkCoreFeature).GetProperties(),
            property => property.Name == nameof(RuntimeArtifactsEntityFrameworkCoreFeature.RecoveryContinuationSigningKey)
                        && property.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "ManifestSettingAttribute"));

        using var provider = services.BuildServiceProvider();
        using var secondProvider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>();
        var token = first.Encode("ef-test", [1, 2, 3]);
        using var scope = secondProvider.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<IRuntimeRecoveryContinuationCodec>();

        Assert.Equal([1, 2, 3], second.Decode("ef-test", token));
        Assert.False(provider.GetRequiredService<IOptions<RuntimeRecoveryContinuationOptions>>().Value.AllowEphemeralDevelopmentKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public void Feature_fails_closed_when_the_durable_signing_key_is_missing_or_short(string? signingKey)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new RuntimeArtifactsEntityFrameworkCoreFeature
        {
            RecoveryContinuationSigningKey = signingKey
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>());
        Assert.Throws<InvalidOperationException>(() => provider.GetServices<IStartupTask>().ToArray());
    }

    [Fact]
    public void Feature_can_be_extended_without_replacing_its_registration_contract()
    {
        var feature = new ExtensibleFeature
        {
            RecoveryContinuationSigningKey = SigningKey
        };
        var services = new ServiceCollection();

        feature.ConfigureServices(services);

        Assert.True(feature.WasConfigured);
        Assert.IsAssignableFrom<RuntimeArtifactsEntityFrameworkCoreFeature>(feature);
    }

    [Fact]
    public void Artifact_registration_rejects_instance_owned_contracts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowExecutableStore>(new InMemoryWorkflowExecutableStore());

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeArtifactsEntityFrameworkCore(new()));
    }

    [Fact]
    public void Bookmark_feature_can_be_extended_without_replacing_its_registration_contract()
    {
        var feature = new ExtensibleBookmarksFeature();
        var services = new ServiceCollection();

        feature.ConfigureServices(services);

        Assert.True(feature.WasConfigured);
        Assert.IsAssignableFrom<RuntimeBookmarksEntityFrameworkCoreFeature>(feature);
    }

    private sealed class ExtensibleFeature : RuntimeArtifactsEntityFrameworkCoreFeature
    {
        public bool WasConfigured { get; private set; }

        public override void ConfigureServices(IServiceCollection services)
        {
            WasConfigured = true;
            base.ConfigureServices(services);
        }
    }

    private sealed class ExtensibleBookmarksFeature : RuntimeBookmarksEntityFrameworkCoreFeature
    {
        public bool WasConfigured { get; private set; }

        public override void ConfigureServices(IServiceCollection services)
        {
            WasConfigured = true;
            base.ConfigureServices(services);
        }
    }
}
