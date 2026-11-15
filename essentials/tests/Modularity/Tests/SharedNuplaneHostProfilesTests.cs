using CShells.Nuplane;
using FoundationHost = Elsa.Foundation.Host.Shells;
using WorkbenchHost = Elsa.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class SharedNuplaneHostProfilesTests
{
    [Theory(DisplayName = "Foundation's legacy key maps to its every-completion profile")]
    [InlineData(null, true)]
    [InlineData("invalid", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Foundation_profile_UsesLegacyEnabledFallback_AndEveryEligibleCompletion(string? setting, bool enabled)
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration(setting);
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: true);
        var options = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue;

        Assert.Equal(enabled, options.Enabled);
        Assert.True(options.AutoReload);
        Assert.Equal(NuplaneRefreshTrigger.EveryEligibleCompletion, options.RefreshTrigger);
        Assert.NotNull(options.OnReloadResults);
    }

    [Theory(DisplayName = "Workbench's legacy key maps to changed-or-pending refresh and opt-in reload")]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Workbench_profile_UsesEnabledRefresh_AndLegacyAutoReloadFallback(string? setting, bool autoReload)
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration(setting);
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: false);
        var options = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue;

        Assert.True(options.Enabled);
        Assert.Equal(autoReload, options.AutoReload);
        Assert.Equal(NuplaneRefreshTrigger.ChangedOrPending, options.RefreshTrigger);
        Assert.NotNull(options.OnReloadResults);
    }

    [Fact(DisplayName = "The next options read sees a reloaded configuration root")]
    public void ConfigurationReload_UpdatesTheMonitoredHostProfile()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("false");
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: false);
        var monitor = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>();

        Assert.False(monitor.CurrentValue.AutoReload);
        configuration[WorkbenchHost.NuplaneIntegrationOptionsSetup.ReloadOnPackageChangeKey] = "true";
        configuration.Reload();

        Assert.True(monitor.CurrentValue.AutoReload);
        Assert.True(monitor.CurrentValue.Enabled);
        Assert.Equal(NuplaneRefreshTrigger.ChangedOrPending, monitor.CurrentValue.RefreshTrigger);
    }



}
