using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Minimal <see cref="IActivity"/> used as a CLR-load target in the factory tests —
/// top-level (not nested) so it resolves as a normal CLR activity type.
/// </summary>
public sealed class NoopActivity : IActivity
{
    public string Id { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string? Name { get; set; }
    public string Type { get; set; } = "Test.Noop";
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, object> CustomProperties { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = [];
    public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => new(true);
    public ValueTask<ActivityTransition> ExecuteAsync(IActivityExecutionContext context) =>
        ValueTask.FromResult<ActivityTransition>(ActivityTransition.Complete(ActivityUnit.Value));
}
