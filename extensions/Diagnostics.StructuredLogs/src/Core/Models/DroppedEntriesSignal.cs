namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// Signals to a live subscriber that some entries were not delivered because the subscriber fell behind
/// and the store evicted them (backpressure). Carried in-band on the live feed so a slow consumer learns
/// of loss without a separate channel.
/// </summary>
public sealed record DroppedEntriesSignal(long DroppedCount, DateTimeOffset Since);
