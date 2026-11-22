using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>Projects a module's actual input-source restrictions without changing hashed catalog payloads.</summary>
/// <remarks>
/// The version and input key are resolved from the real candidate/catalog, never a client expected type
/// or UI hint. A null contribution adds no restriction. The owning compiler remains final authority.
/// </remarks>
public interface IActivityInputSourceConstraintProvider
{
    ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(
        IActivityDefinitionVersion activityVersion,
        string inputKey,
        CancellationToken cancellationToken = default);
}
