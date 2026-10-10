using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

/// <summary>
/// Forces construction of the hierarchy cursor protector during durable runtime startup so a missing shared key
/// fails shell activation instead of invalidating every cursor after a restart or on another node.
/// </summary>
public sealed class ValidateActivityExecutionHierarchyCursorCodecStartupTask : IStartupTask
{
    public ValidateActivityExecutionHierarchyCursorCodecStartupTask(IActivityExecutionHierarchyCursorCodec codec) =>
        ArgumentNullException.ThrowIfNull(codec);

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
