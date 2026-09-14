using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
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
}
