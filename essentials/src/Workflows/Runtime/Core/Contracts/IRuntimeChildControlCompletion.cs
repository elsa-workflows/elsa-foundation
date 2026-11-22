using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Additive result projection for a resumed leaf whose owning activity validated its exact child-control source.
/// The runtime copies this data into the same completion checkpoint; it grants no consumer or dispatch authority.
/// </summary>
public interface IRuntimeChildControlCompletion
{
    RuntimeChildControlTransport? ChildControl { get; }
}
