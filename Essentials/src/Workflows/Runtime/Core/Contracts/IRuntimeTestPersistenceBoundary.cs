using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Qualifies the actual physical stores, persistence scope and admitted write formats together. Provider labels or
/// durability declarations alone cannot enable test execution. No provider is selected implicitly.
/// </summary>
public interface IRuntimeTestPersistenceBoundary
{
    bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context);
}
