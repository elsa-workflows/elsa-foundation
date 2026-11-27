namespace Elsa.Diagnostics.StructuredLogs.Core.Exceptions;

/// <summary>
/// Reports that a replay cursor cannot safely continue in the current bound store. The deliberately
/// uniform message does not distinguish malformed, expired, trimmed, wrong-scope or wrong-stream values.
/// </summary>
public sealed class StructuredLogReplayCursorUnavailableException()
    : StructuredLogsException("The structured log replay cursor is unavailable.");
