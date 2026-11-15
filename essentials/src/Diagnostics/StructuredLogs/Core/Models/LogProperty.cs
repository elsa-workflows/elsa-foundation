namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A single captured property attached to a log entry (e.g. a structured message argument).
/// </summary>
public sealed record LogProperty(string Name, string Value);
