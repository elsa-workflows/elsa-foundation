namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Narrow deduplicator for the stimulus START path (W7, Condition A). In an at-least-once delivery world a
/// stimulus can be delivered more than once; when the caller supplies an idempotency key the router asks this
/// service whether a start for that key has already been admitted, so a duplicate delivery does not silently
/// double-start a workflow.
/// </summary>
/// <remarks>
/// This is intentionally NOT a durable dedup store. The default in-process implementation dedups within a
/// single process for its lifetime only; a restart or a second host forgets prior keys. That is an accepted
/// limit for this wave (the ruling explicitly declined a heavy dedup store) — hosts that need cross-process or
/// restart-durable start-once semantics replace this contract. When no idempotency key is supplied the router
/// does not consult this service at all and the start path is plainly at-least-once.
/// </remarks>
public interface IStimulusStartDeduplicator
{
    /// <summary>
    /// Atomically records an intent to start under <paramref name="idempotencyKey"/>. Returns <c>true</c> the
    /// first time a key is seen (the start may proceed) and <c>false</c> for every subsequent duplicate.
    /// </summary>
    bool TryBeginStart(string idempotencyKey);
}
