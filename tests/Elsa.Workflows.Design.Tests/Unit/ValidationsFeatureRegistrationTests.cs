using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Framework §2.23.1 + Unit C SC-021. Activates <see cref="WorkflowDesignValidationsFeature"/>
/// against a real <see cref="IServiceCollection"/>, builds the provider, and asserts every
/// baseline validator (FR-033) resolves as both <c>IDomainEventHandler</c> and
/// <c>IDomainEventHandler&lt;OnDraftValidating&gt;</c>.
/// </summary>
public sealed class ValidationsFeatureRegistrationTests
{
    [Fact]
    public void Feature_registers_all_five_baseline_validators()
    {
        using var provider = BuildProvider(_ => { });

        var handlers = provider.GetServices<IDomainEventHandler>().ToList();
        var validatorTypes = handlers.Select(h => h.GetType()).ToList();

        Assert.Contains(typeof(OrphanActivityValidator), validatorTypes);
        Assert.Contains(typeof(StartActivityValidator), validatorTypes);
        Assert.Contains(typeof(VariableUniquenessValidator), validatorTypes);
        Assert.Contains(typeof(RequiredInputOutputValidator), validatorTypes);
        Assert.Contains(typeof(VariableExpressionResolverValidator), validatorTypes);
    }

    [Fact]
    public void All_validators_implement_IDomainEventHandler_of_OnDraftValidating()
    {
        using var provider = BuildProvider(_ => { });

        var handlers = provider.GetServices<IDomainEventHandler>().ToList();

        Assert.All(handlers, h =>
            Assert.IsAssignableFrom<IDomainEventHandler<OnDraftValidating>>(h));
    }

    [Fact]
    public void Options_bind_with_default_MaxRecursionDepth_of_100()
    {
        using var provider = BuildProvider(_ => { });

        var options = provider.GetRequiredService<IOptions<WorkflowDesignValidatorOptions>>().Value;

        Assert.Equal(100, options.MaxRecursionDepth);
    }

    [Fact]
    public void Feature_property_overrides_MaxRecursionDepth_on_the_bound_options()
    {
        using var provider = BuildProvider(feature => feature.MaxRecursionDepth = 7);

        var options = provider.GetRequiredService<IOptions<WorkflowDesignValidatorOptions>>().Value;

        Assert.Equal(7, options.MaxRecursionDepth);
    }

    private static ServiceProvider BuildProvider(Action<WorkflowDesignValidationsFeature> configureFeature)
    {
        var feature = new WorkflowDesignValidationsFeature();
        configureFeature(feature);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<IActivityDefinitionLookup, StubActivityDefinitionLookup>();
        feature.ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private sealed class StubActivityDefinitionLookup : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
