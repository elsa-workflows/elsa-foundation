using CShells.Lifecycle;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Studio.Dashboard.QueryTests;

public sealed class DashboardManagementCompositionTests
{
    private static readonly Type[] Replacements =
    [
        typeof(IDashboardQueryService), typeof(IDashboardResourceService), typeof(IDashboardDefinitionValidator),
        typeof(IDashboardResourceStore), typeof(IDashboardActorContext), typeof(IDashboardAudienceDirectory),
        typeof(IDashboardAuthoringService)
    ];

    public static TheoryData<Type, bool> ConflictingRegistrations
    {
        get
        {
            var cases = new TheoryData<Type, bool>();
            foreach (var contract in Replacements)
            {
                cases.Add(contract, false);
                cases.Add(contract, true);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ConflictingRegistrations))]
    public async Task Independent_management_composition_refuses_ambiguous_replacements_before_or_after_feature_registration(Type contract, bool before)
    {
        var services = new ServiceCollection();
        if (!before) new DashboardManagementFeature().ConfigureServices(services);
        services.RemoveAll(contract);
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("A rejected replacement must never be constructed.")));
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("A rejected replacement must never be constructed.")));
        if (before) new DashboardManagementFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeAsync(provider));

        Assert.Contains(contract.Name, error.Message, StringComparison.Ordinal);
        Assert.Contains("found 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Independent_management_allows_its_query_services_without_optional_api_persistence_or_authoring_composition()
    {
        var services = new ServiceCollection();
        var feature = new DashboardManagementFeature();
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IShellInitializer>());
        await InitializeAsync(provider);
    }

    [Fact]
    public async Task Independent_management_preserves_single_host_replacements_without_constructing_their_collaborators_at_startup()
    {
        var services = new ServiceCollection();
        foreach (var contract in Replacements)
            services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("A cardinality check must not construct request services.")));
        new DashboardManagementFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        await InitializeAsync(provider);
        foreach (var contract in Replacements)
            Assert.Single(services, descriptor => descriptor.ServiceType == contract);
    }

    private static async Task InitializeAsync(IServiceProvider provider)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);
    }
}
