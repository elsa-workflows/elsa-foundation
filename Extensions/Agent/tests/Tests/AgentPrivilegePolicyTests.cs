using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class AgentPrivilegePolicyTests
{
    [Fact]
    public void A_host_without_configuration_remains_default_off_without_requiring_an_effects_feature()
    {
        using var root = AgentTestServices.Create();
        using var scope = root.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IConfiguration>());
        Assert.Equal(new(false, false, false), scope.ServiceProvider.GetRequiredService<AgentPrivilegePolicy>().Read());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Each_host_class_is_bound_independently_from_the_completed_containers_configuration(bool administrative, bool irrecoverable, bool activation)
    {
        var configuration = Configuration(administrative, irrecoverable, activation);
        using var root = AgentTestServices.Create(services =>
        {
            // Configuration is deliberately supplied after the base feature's options registrations.
            services.AddSingleton<IConfiguration>(configuration);
        });
        using var scope = root.CreateScope();
        Assert.Equal(new(administrative, irrecoverable, activation), scope.ServiceProvider.GetRequiredService<AgentPrivilegePolicy>().Read());
    }

    [Fact]
    public void A_reload_changes_the_existing_scoped_observation_without_mutating_an_earlier_snapshot()
    {
        var configuration = Configuration(false, false, false);
        using var root = AgentTestServices.Create(services =>
        {
            services.AddSingleton<IConfiguration>(configuration);
            services.AddFoundationAgentAbstractions();
            services.AddFoundationAgentAbstractions();
        });
        using var scope = root.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<AgentPrivilegePolicy>();
        var before = policy.Read();
        var notifications = 0;
        using var listener = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<AgentPrivilegePolicyOptions>>().OnChange((_, _) => notifications++);
        configuration[Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = "true";
        configuration[Key(nameof(AgentPrivilegePolicyOptions.Activation))] = "true";
        configuration.Reload();
        Assert.Equal(new(true, false, true), policy.Read());
        Assert.Equal(new(false, false, false), before);
        Assert.Equal(1, notifications);
        configuration[Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = "false";
        configuration.Reload();
        Assert.Equal(new(false, false, true), policy.Read());
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(nameof(AgentPrivilegePolicyOptions.Administrative))]
    [InlineData(nameof(AgentPrivilegePolicyOptions.Irrecoverable))]
    [InlineData(nameof(AgentPrivilegePolicyOptions.Activation))]
    public void Invalid_class_configuration_is_diagnosed_without_disclosing_the_supplied_value(string name)
    {
        var canary = "configuration-canary-" + Guid.NewGuid().ToString("N");
        var configuration = Configuration(false, false, false);
        configuration[Key(name)] = canary;
        using var root = AgentTestServices.Create(services => services.AddSingleton<IConfiguration>(configuration));
        using var scope = root.CreateScope();
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<AgentPrivilegePolicy>().Read());
        Assert.Contains(Key(name), error.Message);
        Assert.DoesNotContain(canary, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_host_options_configuration_works_without_an_IConfiguration_registration()
    {
        using var root = AgentTestServices.Create(services => services.Configure<AgentPrivilegePolicyOptions>(options => options.Irrecoverable = true));
        using var scope = root.CreateScope();
        Assert.Equal(new(false, true, false), scope.ServiceProvider.GetRequiredService<AgentPrivilegePolicy>().Read());
    }

    private static string Key(string name) => $"{AgentPrivilegePolicyOptions.SectionName}:{name}";
    private static IConfigurationRoot Configuration(bool administrative, bool irrecoverable, bool activation) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = administrative.ToString(),
            [Key(nameof(AgentPrivilegePolicyOptions.Irrecoverable))] = irrecoverable.ToString(),
            [Key(nameof(AgentPrivilegePolicyOptions.Activation))] = activation.ToString()
        }).Build();
}
