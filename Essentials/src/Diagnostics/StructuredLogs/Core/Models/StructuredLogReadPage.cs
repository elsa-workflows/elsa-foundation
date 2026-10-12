namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// One bounded, oldest-first durable-tail read. <see cref="NextCursor"/> is the last committed position
/// scanned, even when its record did not match the filter. <see cref="HasMore"/> means the same snapshot
/// contains later positions and should be drained immediately.
/// </summary>
public sealed record StructuredLogReadPage(
    IReadOnlyList<StructuredLogEntry> Entries,
    StructuredLogReplayCursor? NextCursor,
    bool HasMore);
