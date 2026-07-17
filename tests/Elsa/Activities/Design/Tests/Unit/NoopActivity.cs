using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Minimal <see cref="IActivity"/> used as a CLR-load target in the factory tests —
/// top-level (not nested) so it resolves as a normal CLR activity type.
/// </summary>
public sealed class NoopActivity : IActivity
{
    public ValueTask<ActivityTransition> ExecuteAsync(IActivityExecutionContext context) =>
        ValueTask.FromResult<ActivityTransition>(ActivityTransition.Complete(ActivityUnit.Value));
}
