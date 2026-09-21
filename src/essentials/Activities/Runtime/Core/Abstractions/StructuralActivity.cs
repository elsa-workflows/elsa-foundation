using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Abstractions;

/// <summary>
/// Engine-only base for structural activities. Structural execution is available exclusively through
/// the runtime structural protocols; ordinary activity invocation is rejected instead of completing silently.
/// </summary>
public abstract class StructuralActivity : IActivity, IActivityResult<ActivityUnit>
{
    ValueTask<ActivityTransition> IActivity.ExecuteAsync(ActivityExecutionContext context) =>
        throw new InvalidOperationException(
            $"Structural activity '{GetType().FullName}' must be invoked through its runtime structural protocol.");
}
