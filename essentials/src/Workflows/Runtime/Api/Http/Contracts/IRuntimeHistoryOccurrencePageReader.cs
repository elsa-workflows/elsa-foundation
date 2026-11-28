using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Serves the selected, authorized activity occurrence reader for the shell.</summary>
public interface IRuntimeHistoryOccurrencePageReader
{
    Task<RuntimeHistoryOccurrencePageView?> ReadAsync(GetRuntimeHistoryOccurrences request, CancellationToken cancellationToken = default);
}
