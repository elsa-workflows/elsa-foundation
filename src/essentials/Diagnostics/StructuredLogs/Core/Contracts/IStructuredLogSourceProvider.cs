using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Describes the log sources known to this host. v1 returns a single local source; the shape is retained
/// so future remote sources fit without a contract change.
/// </summary>
public interface IStructuredLogSourceProvider
{
    /// <summary>Returns the local host source that captured entries are stamped with.</summary>
    LogSource GetLocalSource();

    /// <summary>Returns all sources a UI can offer for selection (v1: just the local source).</summary>
    IReadOnlyList<LogSource> GetKnownSources();
}
