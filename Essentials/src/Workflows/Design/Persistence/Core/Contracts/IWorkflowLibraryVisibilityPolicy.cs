using System.Security.Claims;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Supplies a complete, bounded query restriction for a trusted normalized reader. Hosts with
/// resource-specific permission rules must supply this port so rows, counts and cursors share
/// the same authorized universe; post-filtering an already-paged inventory is insufficient.
/// This is an optional replacement contract: select at most one implementation per application context.
/// </summary>
public interface IWorkflowLibraryVisibilityPolicy
{
    ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
