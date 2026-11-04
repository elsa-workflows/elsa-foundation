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
        typeof(IActivityCatalogProjection), typeof(IActivityPublicationVersionPolicy)
    }.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task A_late_second_replacement_is_refused_before_startup_publication(Type contract)
    {
        IServiceCollection services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("Admission must not instantiate a disputed service.")));
        Assert.Contains(services, x => x.ServiceType == typeof(IStartupTask) && x.ImplementationType == typeof(ValidateActivityPublishingReplacementContractsStartupTask));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ValidateActivityPublishingReplacementContractsStartupTask(services).ExecuteAsync(default));
        Assert.Contains(contract.Name + " (2 registrations)", error.Message);
        Assert.Contains("services.Replace", error.Message);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task One_explicit_replacement_and_repeated_feature_composition_remain_valid(Type contract)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("Admission must count registrations without activating them.")));
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        await new ValidateActivityPublishingReplacementContractsStartupTask(services).ExecuteAsync(default);
        services.Replace(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException()));
        await new ValidateActivityPublishingReplacementContractsStartupTask(services).ExecuteAsync(default);
    }
}
