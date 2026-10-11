using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Executes one capability-limited incident-resolution decision.</summary>
public interface IIncidentResolutionAction
{
    string Kind { get; }
    ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken);
}
