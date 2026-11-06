using CShells.Lifecycle;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Activates <see cref="WorkflowDesignValidationsFeature"/>
/// against a real <see cref="IServiceCollection"/>, builds the provider, and asserts every
/// baseline validator resolves as an <see cref="IDraftValidator"/> and that the feature
/// registers exactly one <c>IEventHandler&lt;DraftValidating&gt;</c> — the aggregating
/// <see cref="ExecuteValidations"/> handler.
/// </summary>
public sealed class ValidationsFeatureRegistrationTests
{
    // Kept beside the composition test: asserts the retired orphan validator stays unregistered.
    [Fact]
    public void Feature_registers_all_five_baseline_validators()
    {
        using var provider = BuildProvider(_ => { });

        var validatorTypes = provider.GetServices<IDraftValidator>().Select(v => v.GetType()).ToList();

        Assert.Contains(typeof(UnknownActivityVersionValidator), validatorTypes);
        Assert.Contains(typeof(StartActivityValidator), validatorTypes);
        Assert.Contains(typeof(VariableUniquenessValidator), validatorTypes);
        Assert.Contains(typeof(RequiredInputOutputValidator), validatorTypes);
        Assert.Contains(typeof(VariableExpressionResolverValidator), validatorTypes);
        Assert.DoesNotContain(validatorTypes, type => type.Name.Contains("Orphan", StringComparison.OrdinalIgnoreCase));
    }

    // Kept beside the composition test: uniqueness, exactly one DraftValidating handler.
    [Fact]
    public void Feature_registers_exactly_one_DraftValidating_handler()
    {
        using var provider = BuildProvider(_ => { });

        var handler = Assert.Single(provider.GetServices<IEventHandler>());
        Assert.IsType<ExecuteValidations>(handler);
        Assert.IsAssignableFrom<IEventHandler<DraftValidating>>(handler);
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

    // Kept beside the composition test: forwarding, one credential literal validator instance behind both contracts.
    [Fact]
    public void Feature_registers_the_credential_literal_validator_once_as_its_contract_and_as_a_draft_validator()
    {
        using var provider = BuildProvider(_ => { });
        using var scope = provider.CreateScope();

        var contract = Assert.Single(scope.ServiceProvider.GetServices<ICredentialLiteralValidator>());
        Assert.IsType<CredentialLiteralValidator>(contract);
        Assert.Same(contract, Assert.Single(scope.ServiceProvider.GetServices<IDraftValidator>().OfType<CredentialLiteralValidator>()));
    }

    [Fact]
    public void The_credential_literal_validator_is_declared_a_replacement_contract() =>
        Assert.True(typeof(ICredentialLiteralValidator).IsDefined(typeof(CredentialLiteralValidatorReplacementContractAttribute), inherit: false));

    [Fact]
    public async Task A_host_composing_one_credential_literal_validator_starts()
    {
        using var provider = BuildProvider(_ => { });

        Assert.Null(await Record.ExceptionAsync(() => InitializeShellAsync(provider)));
    }

    // Kept beside the composition test: ordering, a second validator fails activation in either registration order.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_credential_literal_validator_fails_shell_activation_whatever_the_registration_order(bool registeredBefore)
    {
        using var provider = BuildProvider(_ => { }, services => services.AddScoped<ICredentialLiteralValidator, SecondValidator>(), registeredBefore);

        var failure = await Assert.ThrowsAsync<MultipleCredentialLiteralValidatorsException>(() => InitializeShellAsync(provider));

        Assert.Contains(nameof(ICredentialLiteralValidator), failure.Message, StringComparison.Ordinal);
        Assert.Contains($"'{typeof(SecondValidator).FullName}'", failure.Implementations);
        Assert.Contains("a factory registration", failure.Implementations);
    }

    // Kept beside the composition test: replacement, a host replacing the default after the feature registers.
    [Fact]
    public async Task A_host_that_replaces_the_default_after_the_feature_registers_starts_and_resolves_its_replacement()
    {
        using var provider = BuildProvider(_ => { }, services => services.Replace(ServiceDescriptor.Scoped<ICredentialLiteralValidator, SecondValidator>()));

        Assert.Null(await Record.ExceptionAsync(() => InitializeShellAsync(provider)));
        using var scope = provider.CreateScope();
        Assert.IsType<SecondValidator>(Assert.Single(scope.ServiceProvider.GetServices<ICredentialLiteralValidator>()));
        Assert.Single(scope.ServiceProvider.GetServices<IDraftValidator>().OfType<CredentialLiteralValidator>());
    }

    private static async Task InitializeShellAsync(IServiceProvider provider)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    [Fact]
    public void Source_analysis_has_one_scoped_authority()
    {
        using var provider = BuildProvider(_ => { });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var analyzer = Assert.Single(first.ServiceProvider.GetServices<IActivityDataSelectionAnalyzer>());
        Assert.Same(analyzer, first.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>());
        Assert.NotSame(analyzer, second.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_source_authority_fails_activation_in_either_registration_order(bool registeredBefore)
    {
        using var provider = BuildProvider(_ => { }, services => {
            services.AddScoped<IActivityDataSelectionAnalyzer, SecondAnalyzer>();
            services.AddScoped<IActivityDataSelectionAnalyzer, SecondAnalyzer>();
        }, registeredBefore);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeShellAsync(provider));
        Assert.Contains(nameof(IActivityDataSelectionAnalyzer), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pre_registered_scoped_source_authority_is_preserved_as_the_one_host_replacement()
    {
        using var provider = BuildProvider(_ => { }, services => services.AddScoped<IActivityDataSelectionAnalyzer, SecondAnalyzer>(), hostRegistersFirst: true);
        await InitializeShellAsync(provider);
        using var scope = provider.CreateScope();
        Assert.IsType<SecondAnalyzer>(Assert.Single(scope.ServiceProvider.GetServices<IActivityDataSelectionAnalyzer>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_singleton_source_authority_fails_activation(bool replaceWithSingleton)
    {
        using var provider = BuildProvider(_ => { }, services => {
            services.RemoveAll<IActivityDataSelectionAnalyzer>();
            if (replaceWithSingleton) services.AddSingleton<IActivityDataSelectionAnalyzer, SecondAnalyzer>();
        });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeShellAsync(provider));
        Assert.Contains(nameof(IActivityDataSelectionAnalyzer), failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("singleton")]
    public async Task A_copied_child_shell_checks_its_own_source_authority_and_preserves_the_parent(string invalid)
    {
        IServiceCollection parent = new ServiceCollection();
        new WorkflowDesignValidationsFeature().ConfigureServices(parent);
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        new WorkflowDesignValidationsFeature().ConfigureServices(child);
        if (invalid == "duplicate") child.AddScoped<IActivityDataSelectionAnalyzer, SecondAnalyzer>();
        if (invalid != "duplicate") child.RemoveAll<IActivityDataSelectionAnalyzer>();
        if (invalid == "singleton") child.AddSingleton<IActivityDataSelectionAnalyzer, SecondAnalyzer>();
        using var parentProvider = parent.BuildServiceProvider();
        using var childProvider = child.BuildServiceProvider();

        await SourceInitializer(parentProvider).InitializeAsync();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SourceInitializer(childProvider).InitializeAsync());
        Assert.Contains(nameof(IActivityDataSelectionAnalyzer), failure.Message, StringComparison.Ordinal);
        Assert.Single(parent, descriptor => descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer));
    }

    [Fact]
    public async Task A_copied_child_shell_can_keep_one_scoped_source_authority()
    {
        IServiceCollection parent = new ServiceCollection();
        new WorkflowDesignValidationsFeature().ConfigureServices(parent);
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        new WorkflowDesignValidationsFeature().ConfigureServices(child);
        using var parentProvider = parent.BuildServiceProvider();
        using var childProvider = child.BuildServiceProvider();

        await SourceInitializer(parentProvider).InitializeAsync();
        await SourceInitializer(childProvider).InitializeAsync();
        Assert.Single(child, descriptor => descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer));
    }

    private static IShellInitializer SourceInitializer(IServiceProvider provider) => Assert.Single(
        provider.GetServices<IShellInitializer>(), initializer => initializer.GetType().Name == "ActivityDataSelectionAnalyzerCompositionCheck");

    private static ServiceProvider BuildProvider(
        Action<WorkflowDesignValidationsFeature> configureFeature,
        Action<IServiceCollection>? hostRegistrations = null,
        bool hostRegistersFirst = false)
    {
        var feature = new WorkflowDesignValidationsFeature();
        configureFeature(feature);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<IActivityDefinitionLookup>(new BaselineValidatorTests.StubActivityCatalog());
        if (hostRegistersFirst)
            hostRegistrations?.Invoke(services);
        feature.ConfigureServices(services);
        if (!hostRegistersFirst)
            hostRegistrations?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class SecondValidator : ICredentialLiteralValidator
    {
        public ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ValidationError>>([]);
    }

    private sealed class SecondAnalyzer : IActivityDataSelectionAnalyzer
    {
        public ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document,
            ActivityDataSelectionTarget? target = null, ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDataSelectionAnalysis(document.ContractRevision, null, [], []));
    }
}
