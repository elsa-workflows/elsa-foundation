using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Supplies the immutable persistence access selected for the current dependency-injection scope.</summary>
public interface IPersistenceAccessContextAccessor
{
    PersistenceAccessContext Current { get; }
}
