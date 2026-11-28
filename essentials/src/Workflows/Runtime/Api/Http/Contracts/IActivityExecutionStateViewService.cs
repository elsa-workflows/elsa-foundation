using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>The activity state view operation the runtime endpoint dispatches to.</summary>
public interface IActivityExecutionStateViewService
{
    Task<ActivityStateView?> GetAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken);
}
