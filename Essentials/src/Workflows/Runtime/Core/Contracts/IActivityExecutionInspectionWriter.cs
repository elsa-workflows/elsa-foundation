using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IActivityExecutionInspectionWriter
{
    ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default);
}
