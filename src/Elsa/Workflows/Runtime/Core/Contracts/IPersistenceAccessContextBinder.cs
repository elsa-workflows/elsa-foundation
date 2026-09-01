using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Binds an explicitly transported access context to a newly created dependency-injection scope.
/// Host-provided accessors that replace the default must provide a binder backed by the same scoped state.
/// </summary>
public interface IPersistenceAccessContextBinder
{
    void Bind(PersistenceAccessContext context);
}
