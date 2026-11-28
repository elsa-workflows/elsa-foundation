using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Serves the standard history page from the shell's selected run store.</summary>
public interface IRuntimeHistoryPageReader
{
    /// <summary>Returns a page after authorizing the exact run, or null when the run is unavailable to this caller.</summary>
    Task<RuntimeHistoryPageView?> ReadAsync(GetRuntimeHistory request, CancellationToken cancellationToken = default);
}
