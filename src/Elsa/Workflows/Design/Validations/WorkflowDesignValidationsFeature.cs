using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Core.Extensions;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Validations;

/// <summary>
/// Activation unit for the workflow-design validations sub-domain (Unit C FR-032). Registers the
/// single <see cref="ExecuteValidations"/> handler for <c>OnDraftValidating</c> plus the baseline
/// universal validators (FR-033) — unknown-activity-version, unhandled-activity-structure,
/// missing root activity, variable-uniqueness, required-input/output,
/// variable-expression-resolver, value-flow — as <see cref="IDraftValidator"/>
/// implementations. The handler resolves every <see cref="IDraftValidator"/> and aggregates their
/// errors. Activity-specific validators ship in their activity feature (FR-034) by registering
/// their own <see cref="IDraftValidator"/>.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Validation")]
[ShellFeature(
    name: "WorkflowDesignValidations",
    DisplayName = "Workflow Design Validations",
    Description = "Registers baseline workflow draft validators and validation event handling."
)]
public class WorkflowDesignValidationsFeature : IShellFeature
{
    /// <summary>
    /// Maximum tree depth the activity-tree walker descends while validating. Bound to
    /// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>. Default: 100.
    /// </summary>
    [ManifestSetting(DisplayName = "Maximum recursion depth", Description = "Maximum activity tree depth inspected by workflow draft validation.", Category = "Validation", DefaultValue = "100")]
    public int MaxRecursionDepth { get; set; } = 100;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<WorkflowDesignValidatorOptions>(options =>
        {
            options.MaxRecursionDepth = MaxRecursionDepth;
        });

        services.AddEventHandler<OnDraftValidating, ExecuteValidations>();

        services.AddScopedVariableAuthoring();
        services.AddScoped<ActivityTreeWalker>();
        services.AddScoped<CatalogVersionResolver>();
        services.AddScoped<ScopedVariableReferenceRemapper>();
        services.AddScoped<IDraftValidator, UnknownActivityVersionValidator>();
        services.AddScoped<IDraftValidator, UnhandledActivityStructureValidator>();
        services.AddScoped<IDraftValidator, StartActivityValidator>();
        services.AddScoped<IDraftValidator, VariableUniquenessValidator>();
        services.AddScoped<IDraftValidator, RequiredInputOutputValidator>();
        services.AddScoped<IDraftValidator, VariableExpressionResolverValidator>();
        services.AddScoped<IDraftValidator, IntrinsicVariableTargetValidator>();
        services.AddScoped<IDraftValidator, ValueFlowValidator>();
        services.TryAddSingleton<IExpressionToolingProviderResolver>(new UnavailableExpressionToolingProviderResolver());
        services.AddScoped<IExpressionDraftSemanticValidator, ExpressionDraftSemanticValidator>();
        services.AddScoped<IDraftValidator, ExpressionDraftValidator>();
    }

    private sealed class UnavailableExpressionToolingProviderResolver : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) => null;
    }
}
