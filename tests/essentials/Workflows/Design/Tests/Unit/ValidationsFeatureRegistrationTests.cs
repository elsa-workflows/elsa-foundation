using CShells.Lifecycle;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Framework §2.23.1 + Unit C SC-021. Activates <see cref="WorkflowDesignValidationsFeature"/>
/// against a real <see cref="IServiceCollection"/>, builds the provider, and asserts every
/// baseline validator (FR-033) resolves as an <see cref="IDraftValidator"/> and that the feature
/// registers exactly one <c>IEventHandler&lt;DraftValidating&gt;</c> — the aggregating
/// <see cref="ExecuteValidations"/> handler.
/// </summary>
public sealed class ValidationsFeatureRegistrationTests
{
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

    [Fact]
    public void Feature_registers_the_scoped_variable_services()
    {
        using var provider = BuildProvider(_ => { });

        Assert.NotNull(provider.GetRequiredService<ScopedVariableResolver>());
        Assert.NotNull(provider.GetRequiredService<ScopedVariablePicker>());
        Assert.NotNull(provider.GetRequiredService<ScopedVariableReferenceRemapper>());
        Assert.NotNull(provider.GetRequiredService<ScopedVariableAuthoringContract>());
    }

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

    private static async Task InitializeShellAsync(IServiceProvider provider)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

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
}
