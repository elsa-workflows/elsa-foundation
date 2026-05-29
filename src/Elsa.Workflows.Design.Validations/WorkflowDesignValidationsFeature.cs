using CShells.Features;
using Elsa.Mediator.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations;

/// <summary>
/// Activation unit for the workflow-design validations sub-domain (Unit C FR-032). Registers
/// the baseline universal validators (FR-033) — orphan-activity, missing/duplicate-start,
/// variable-uniqueness, required-input/output, variable-expression-resolver — as
/// <c>IDomainEventHandler&lt;OnDraftValidating&gt;</c> implementations. Activity-specific
/// validators ship in their activity feature (FR-034), not here.
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

        services.AddDomainEventHandlersFrom(typeof(WorkflowDesignValidationsFeature).Assembly);
    }
}
