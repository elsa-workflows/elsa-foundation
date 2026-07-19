using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Engine-only protocol for activities that schedule and coordinate structural children.</summary>
public interface IRuntimeStructuralActivity
{
    ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context);
}

/// <summary>Engine-only protocol for evaluating a structure after one of its children completes.</summary>
public interface IRuntimeActivityChildCompletionHandler
{
    ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context);
}

/// <summary>Engine-only protocol for evaluating a structure after one of its children faults.</summary>
public interface IRuntimeActivityChildFaultHandler
{
    ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context);
}
