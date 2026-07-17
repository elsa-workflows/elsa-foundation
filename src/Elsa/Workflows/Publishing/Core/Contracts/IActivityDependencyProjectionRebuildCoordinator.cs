using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Cross-domain recovery coordinator that reconstructs the complete Activity Design dependency
/// projection from current activity/workflow drafts and immutable versions. Ordinary Workflow Design
/// mutations may leave the derived view behind; invoking this coordinator converges it at a new,
/// externally visible watermark.
/// </summary>
public interface IActivityDependencyProjectionRebuildCoordinator
{
    Task<ActivityDependencyProjectionRebuild> RebuildAsync(CancellationToken cancellationToken = default);
}
