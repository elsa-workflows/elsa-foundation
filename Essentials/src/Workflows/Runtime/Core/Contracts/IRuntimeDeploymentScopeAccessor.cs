using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Independently supplied trusted local target scope. No default or captured-payload fallback is permitted.</summary>
public interface IRuntimeDeploymentScopeAccessor
{
    RuntimeDeploymentScope RequireScope();
}
