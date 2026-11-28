using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// The single-activity-execution inspection operation the runtime endpoints dispatch to. A null view means the
/// workflow execution or activity execution is missing, or the caller may not inspect it.
/// </summary>
public interface IActivityExecutionInspectionService
{
    Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken);
}
