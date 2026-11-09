using System.Reflection;
using CShells.Features;
using CShells.Features.Validation;
using Elsa.Workflows.Runtime.ActivationChannels;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ActivationChannelConfigurationTests
{
    [Fact]
    public void Shell_feature_binds_a_list_of_channel_declarations_and_configures_runtime_options()
    {
        var featureType = typeof(WorkflowsActivationChannelsFeature);
        var featureAttribute = Assert.Single(featureType.GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
            .Cast<ShellFeatureAttribute>());
        var channelsProperty = featureType.GetProperty(nameof(WorkflowsActivationChannelsFeature.Channels))!;
        var featureName = Assert.IsType<string>(featureAttribute.Name);
        Assert.Equal("WorkflowsActivationChannels", featureName);
        Assert.Equal(typeof(List<ActivationChannelDefinition>), channelsProperty.PropertyType);
        Assert.Contains(channelsProperty.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowsActivationChannels:AllowAdHocChannels"] = "false",
                ["WorkflowsActivationChannels:Channels:0:Name"] = "production",
                ["WorkflowsActivationChannels:Channels:0:DisplayName"] = "Production",
                ["WorkflowsActivationChannels:Channels:0:Description"] = "Normal traffic",
                ["WorkflowsActivationChannels:Channels:1:Name"] = "preview",
                ["WorkflowsActivationChannels:Channels:1:DisplayName"] = "Preview"
            })
            .Build();
        var feature = new WorkflowsActivationChannelsFeature();
        new FeatureConfigurationBinder(configuration, new CompositeFeatureConfigurationValidator([]))
            .BindAndConfigure(feature, featureName);

        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ActivationChannelOptions>>().Value;

        Assert.False(options.AllowAdHocChannels);
        Assert.Collection(options.Channels,
            channel =>
            {
                Assert.Equal("production", channel.Name);
                Assert.Equal("Production", channel.DisplayName);
                Assert.Equal("Normal traffic", channel.Description);
            },
            channel =>
            {
                Assert.Equal("preview", channel.Name);
                Assert.Equal("Preview", channel.DisplayName);
                Assert.Null(channel.Description);
            });
        Assert.True(options.Allows("production"));
        Assert.False(options.Allows("Production"));
    }

    [Fact]
    public void Feature_registration_validates_malformed_and_duplicated_channel_declarations()
    {
        var services = new ServiceCollection();
        new WorkflowsActivationChannelsFeature
        {
            AllowAdHocChannels = false,
            Channels =
            [
                new() { Name = "production", DisplayName = " " },
                new() { Name = "production" }
            ]
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ActivationChannelOptions>>().Value);
    }

}
