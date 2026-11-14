using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Independently supplied trusted local target scope. No default or captured-payload fallback is permitted.</summary>
/// <remarks>Replacement port: select exactly one applicable trusted host accessor per scope; reject duplicates before owner I/O/effects. No strategy keys or implicit fallback are defined.</remarks>
public interface IRuntimeDeploymentScopeAccessor
{
    RuntimeDeploymentScope RequireScope();
}
