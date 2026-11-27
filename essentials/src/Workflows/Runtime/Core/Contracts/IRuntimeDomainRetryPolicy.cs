using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeDomainRetryPolicy
{
    RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request);
}
