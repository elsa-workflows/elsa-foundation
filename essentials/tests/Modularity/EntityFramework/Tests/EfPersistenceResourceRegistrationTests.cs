using CShells.Configuration;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.EntityFramework;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Modularity.EntityFramework.Tests.RegistrationShellDefaults))]

namespace Elsa.Modularity.EntityFramework.Tests;

public sealed class EfPersistenceResourceRegistrationTests
{
    [Fact]
    public void Valid_host_declaration_installs_one_runtime_and_one_EF_management_preparer()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFeatureActivationContextPreparer, DefaultPreparer>();
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());

        services.AddEfPersistenceResources(Configuration(), typeof(RegistrationShellDefaults).Assembly);

        Assert.Single(services.Where(x => x.ServiceType == typeof(IShellSettingsPreparer)));
        Assert.Single(services.Where(x => x.ServiceType == typeof(IFeatureActivationContextPreparer)));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.IsType<EfPersistenceShellSettingsPreparer>(provider.GetRequiredService<IShellSettingsPreparer>());
        using var scope = provider.CreateScope();
        Assert.IsType<EfPersistenceActivationContextPreparer>(
            scope.ServiceProvider.GetRequiredService<IFeatureActivationContextPreparer>());
        Assert.IsType<RegistrationShellDefaults>(provider.GetRequiredService<IEfToolingShellDefaults>());
    }

    [Fact]
    public void Missing_host_declaration_fails_before_registration()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddEfPersistenceResources(Configuration(), typeof(string).Assembly));

        Assert.Contains("exactly one shell-default composer", error.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Fact]
    public void Existing_CShells_preparer_or_custom_management_preparer_is_not_silently_replaced()
    {
        var withShellPreparer = new ServiceCollection();
        withShellPreparer.AddSingleton<IShellSettingsPreparer>(new EfPersistenceShellSettingsPreparer(Configuration()));
        Assert.Throws<InvalidOperationException>(() =>
            withShellPreparer.AddEfPersistenceResources(Configuration(), typeof(RegistrationShellDefaults).Assembly));

        var withCustomManagement = new ServiceCollection();
        withCustomManagement.AddScoped<IFeatureActivationContextPreparer, CustomPreparer>();
        Assert.Throws<InvalidOperationException>(() =>
            withCustomManagement.AddEfPersistenceResources(Configuration(), typeof(RegistrationShellDefaults).Assembly));
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().Build();

    [DefaultFeatureActivationContextPreparer]
    private sealed class DefaultPreparer : IFeatureActivationContextPreparer
    {
        public Task<FeatureActivationContext> PrepareAsync(
            FeatureActivationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(context);
    }

    private sealed class CustomPreparer : IFeatureActivationContextPreparer
    {
        public Task<FeatureActivationContext> PrepareAsync(
            FeatureActivationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(context);
    }
}

public sealed class RegistrationShellDefaults : IEfToolingShellDefaults
{
    public void Configure(ShellBuilder builder, IConfiguration configuration) { }
}
