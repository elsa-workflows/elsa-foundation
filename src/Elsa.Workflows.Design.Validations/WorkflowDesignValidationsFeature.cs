using CShells.Features;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations;

/// <summary>
/// Activation unit for the workflow-design validations sub-domain (Unit C FR-032). Registers the
/// single <see cref="ExecuteValidations"/> handler for <c>OnDraftValidating</c> plus the baseline
/// universal validators (FR-033) — orphan-activity, missing/duplicate-start, variable-uniqueness,
/// required-input/output, variable-expression-resolver — as <see cref="IDraftValidator"/>
/// implementations. The handler resolves every <see cref="IDraftValidator"/> and aggregates their
/// errors. Activity-specific validators ship in their activity feature (FR-034) by registering
/// their own <see cref="IDraftValidator"/>.
/// </summary>
[ShellFeature(
    name: "WorkflowDesignValidations"
)]
public class WorkflowDesignValidationsFeature : IShellFeature
{
    /// <summary>
    /// Maximum tree depth the activity-tree walker descends while validating. Bound to
    /// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>. Default: 100.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 100;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<WorkflowDesignValidatorOptions>(options =>
        {
            options.MaxRecursionDepth = MaxRecursionDepth;
        });

        services.AddEventHandler<OnDraftValidating, ExecuteValidations>();

        services.AddScoped<IDraftValidator, OrphanActivityValidator>();
        services.AddScoped<IDraftValidator, StartActivityValidator>();
        services.AddScoped<IDraftValidator, VariableUniquenessValidator>();
        services.AddScoped<IDraftValidator, RequiredInputOutputValidator>();
        services.AddScoped<IDraftValidator, VariableExpressionResolverValidator>();
    }
}
