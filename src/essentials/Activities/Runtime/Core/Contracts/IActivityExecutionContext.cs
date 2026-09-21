using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

public interface IActivityExecutionContext
{
    IActivity Activity { get; }
    CancellationToken CancellationToken { get; }
}
