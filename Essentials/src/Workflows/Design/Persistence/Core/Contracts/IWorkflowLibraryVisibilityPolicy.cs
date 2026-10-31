using System.Security.Claims;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Supplies a complete, bounded query restriction for a trusted normalized reader. Hosts with
/// resource-specific permission rules must supply this port so rows, counts and cursors share
/// the same authorized universe; post-filtering an already-paged inventory is insufficient.
/// </summary>
public interface IWorkflowLibraryVisibilityPolicy
{
    ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
