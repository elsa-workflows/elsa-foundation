using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Runtime;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Features;
using Elsa.Secrets.Workflows.Extensions;
using Elsa.Secrets.Workflows.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// <c>SecretsWorkflows</c> registers exactly one <see cref="IRuntimeSecretResolver"/>, the bridge, and refuses to register
/// it beside another one; a shell that composes another one after it does not start (spec 188, T024 and T027; framework
/// constitution §2.6.2 and §2.23.1). Its dependencies are named as the features that own them are named, so a shell
/// enabling only the bridge composes both.
/// </summary>
public sealed class SecretsWorkflowsFeatureRegistrationTests
{
    private const string ShellName = "secrets-workflows";
    private readonly ServiceCollection _services = new();

    [Fact]
    public void The_feature_registers_exactly_one_secret_resolver_and_it_is_the_bridge()
    {
        new SecretsWorkflowsFeature().ConfigureServices(_services);
        _services.AddSecrets();
        using var provider = _services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IRuntimeSecretResolver));
        Assert.IsType<SecretValueRuntimeResolver>(Assert.Single(scope.ServiceProvider.GetServices<IRuntimeSecretResolver>()));
    }

    [Fact]
    public void Composing_the_bridge_again_keeps_one_registration()
    {
        new SecretsWorkflowsFeature().ConfigureServices(_services);
        _services.AddSecretsWorkflows();

        Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IRuntimeSecretResolver));
    }

    [Fact]
    public void A_second_secret_resolver_is_refused_with_a_diagnostic_naming_both()
    {
        _services.AddScoped<IRuntimeSecretResolver, OtherSecretResolver>();

        var exception = Assert.Throws<InvalidOperationException>(() => new SecretsWorkflowsFeature().ConfigureServices(_services));

        Assert.Contains(typeof(OtherSecretResolver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecretValueRuntimeResolver).FullName!, exception.Message, StringComparison.Ordinal);
        // Refused, not added beside it and not silently kept as the first: the other resolver is still the only one.
        Assert.Equal(typeof(OtherSecretResolver), Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IRuntimeSecretResolver)).ImplementationType);
    }

    [Fact]
    public void A_resolver_registered_beside_the_bridge_is_refused_when_the_bridge_is_composed_again()
    {
        _services.AddSecretsWorkflows();
        _services.AddSingleton<IRuntimeSecretResolver>(new OtherSecretResolver());

        var exception = Assert.Throws<InvalidOperationException>(() => new SecretsWorkflowsFeature().ConfigureServices(_services));

        Assert.Contains($"an instance of '{typeof(OtherSecretResolver).FullName}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shell_enabling_only_the_bridge_composes_the_features_it_depends_on_by_name()
    {
        await using var host = BuildShellHost("SecretsWorkflows");

        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        Assert.Equal(
            ["ActivitiesRuntime", "Secrets", "SecretsWorkflows"],
            shell.ServiceProvider.GetRequiredService<ShellSettings>().EnabledFeatures.Order(StringComparer.Ordinal));
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        Assert.IsType<SecretValueRuntimeResolver>(scope.ServiceProvider.GetRequiredService<IRuntimeSecretResolver>());
    }

    [Fact]
    public async Task A_shell_composing_a_resolver_after_the_bridge_fails_activation_naming_both()
    {
        // The bridge's registration cannot see a resolver registered after it; the runtime's startup check refuses the
        // shell instead of letting the last registration win (framework constitution §2.6.2).
        await using var host = BuildShellHost(CompetingSecretResolverFeature.Name);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var conflict = Assert.IsType<MultipleRuntimeSecretResolversException>(exception as MultipleRuntimeSecretResolversException ?? exception.InnerException);
        Assert.Contains(typeof(SecretValueRuntimeResolver).FullName!, conflict.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(OtherSecretResolver).FullName!, conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>A CShells host whose one shell, <see cref="ShellName"/>, enables <paramref name="feature"/>.</summary>
    private ServiceProvider BuildShellHost(string feature)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"CShells:Shells:{ShellName}:Features:{feature}"] = null })
            .Build();
        _services.AddLogging();
        _services.AddSingleton<IConfiguration>(configuration);
        _services.AddCShells(shells => shells
            .WithAssemblies(
                typeof(SecretsWorkflowsFeature).Assembly,
                typeof(SecretsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(SecretsWorkflowsFeatureRegistrationTests).Assembly)
            .WithConfigurationProvider(configuration));
        return _services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Registers <see cref="OtherSecretResolver"/> after the bridge, which it depends on.</summary>
    [ShellFeature(name: Name, DisplayName = "Test competing secret resolver", Description = "Registers a second secret resolver after the bridge.", DependsOn = new object[] { "SecretsWorkflows" })]
    public sealed class CompetingSecretResolverFeature : IShellFeature
    {
        public const string Name = "TestCompetingSecretResolver";

        public void ConfigureServices(IServiceCollection services) => services.AddScoped<IRuntimeSecretResolver, OtherSecretResolver>();
    }

    private sealed class OtherSecretResolver : IRuntimeSecretResolver
    {
        public ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RuntimeSecretResolution.Failure("NotFound", isRetryable: false));
    }
}
