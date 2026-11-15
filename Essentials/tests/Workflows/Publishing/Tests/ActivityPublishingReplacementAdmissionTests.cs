using Elsa.Tasks.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class ActivityPublishingReplacementAdmissionTests
{
    public static IEnumerable<object[]> Contracts() => new[]
    {
        typeof(IActivityTemplateCompiler), typeof(IActivityPublicationCandidateEvaluator), typeof(IActivityPublicationReadiness),
        typeof(IActivityCatalogProjection), typeof(IActivityPublicationVersionPolicy), typeof(IExecutableStructureNormalizer)
    }.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task A_late_second_replacement_is_refused_before_startup_publication(Type contract)
    {
        IServiceCollection services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("Admission must not instantiate a disputed service.")));
        Assert.Single(services, x => x.ServiceType == typeof(IStartupTask) && (x.ImplementationType == typeof(ValidateActivityPublishingReplacementContractsStartupTask) || x.ImplementationInstance is ValidateActivityPublishingReplacementContractsStartupTask));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveGuard(services).ExecuteAsync(default));
        Assert.Contains(contract.Name + " (2 registrations)", error.Message);
        Assert.Contains("services.Replace", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copied_child_admission_uses_its_own_collection_even_with_parent_metadata(bool childDuplicate)
    {
        IServiceCollection parent = new ServiceCollection();
        parent.AddSingleton<IServiceCollection>(parent);
        new WorkflowsPublishingFeature().ConfigureServices(parent);
        var extra = ServiceDescriptor.Scoped(typeof(IActivityPublicationCandidateEvaluator), _ => throw new InvalidOperationException());
        if (!childDuplicate) parent.Add(extra);
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        if (childDuplicate) child.Add(extra); else child.Remove(extra);
        new WorkflowsPublishingFeature().ConfigureServices(child);
        if (childDuplicate)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveGuard(child).ExecuteAsync(default));
            await ResolveGuard(parent).ExecuteAsync(default);
        }
        else
        {
            await ResolveGuard(child).ExecuteAsync(default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveGuard(parent).ExecuteAsync(default));
        }
    }

    private static IStartupTask ResolveGuard(IServiceCollection services)
    {
        // The feature registers its admission task last. Resolve that actual contribution through
        // DI without importing a Design API or durable persistence feature into the engine host.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return Assert.IsType<ValidateActivityPublishingReplacementContractsStartupTask>(provider.GetRequiredService<IStartupTask>());
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task One_explicit_replacement_and_repeated_feature_composition_remain_valid(Type contract)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("Admission must count registrations without activating them.")));
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        await ResolveGuard(services).ExecuteAsync(default);
        services.Replace(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException()));
        await ResolveGuard(services).ExecuteAsync(default);
    }

    [Fact]
    public async Task Default_normalizer_is_scoped_and_removal_is_refused_without_activating_services()
    {
        IServiceCollection services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        var normalizer = Assert.Single(services, service => service.ServiceType == typeof(IExecutableStructureNormalizer));
        Assert.Equal(ServiceLifetime.Scoped, normalizer.Lifetime);
        Assert.Equal(typeof(ExecutableStructureNormalizer), normalizer.ImplementationType);
        await ResolveGuard(services).ExecuteAsync(default);
        services.Remove(normalizer);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveGuard(services).ExecuteAsync(default));
        Assert.Contains("IExecutableStructureNormalizer (0 registrations)", error.Message);
    }
}
