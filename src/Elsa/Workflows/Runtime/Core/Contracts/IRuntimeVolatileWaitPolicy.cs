using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Decides whether an activity-scoped volatile wait is allowed in the current host/runtime context.
/// </summary>
public interface IRuntimeVolatileWaitPolicy
{
    RuntimeVolatileWaitPolicyDecision Decide(RuntimeVolatileWaitPolicyRequest request);
}
