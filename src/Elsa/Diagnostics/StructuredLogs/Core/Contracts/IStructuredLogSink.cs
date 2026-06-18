using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// The capture-to-store boundary. The capture <c>ILoggerProvider</c> emits entries here; the default sink
/// forwards them to the store, but a persistence slice can interpose by replacing this registration.
/// </summary>
public interface IStructuredLogSink
{
    /// <summary>Emits a captured entry into the diagnostics pipeline.</summary>
    void Emit(StructuredLogEntry entry);
}
