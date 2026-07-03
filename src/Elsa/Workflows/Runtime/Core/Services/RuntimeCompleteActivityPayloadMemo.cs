using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// RT-11: a per-work-item memo of the parsed <see cref="RuntimeCompleteActivityCommandPayload"/>.
/// <para>
/// A CompleteActivity work item's payload is inspected up to four times for a single dispatch — the routing selector,
/// both claiming handlers' <c>CanHandle</c>, and the winning handler's body — each of which deserialized the payload
/// independently. Because the work item is immutable, the deserialization is deterministic, so this memo parses the
/// payload once and hands the same instance to every call site.
/// </para>
/// <para>
/// Only successful parses are cached; a malformed payload throws the underlying deserialization exception (callers keep
/// their own <c>try</c>/<c>catch</c> semantics) and is never cached, so every failure path behaves exactly as before.
/// The key is held weakly, so entries are collected with their work item and no dispatch state leaks.
/// </para>
/// </summary>
public static class RuntimeCompleteActivityPayloadMemo
{
    private static readonly ConditionalWeakTable<RuntimeSchedulerWorkItem, RuntimeCompleteActivityCommandPayload> Cache = new();

    /// <summary>
    /// Returns the work item's parsed CompleteActivity payload, reusing the cached instance when it was parsed earlier
    /// in the same dispatch. Returns <see langword="null"/> when the item carries no payload (or the payload
    /// deserializes to <see langword="null"/>). Propagates the underlying deserialization exception on a malformed
    /// payload without caching it.
    /// </summary>
    public static RuntimeCompleteActivityCommandPayload? Deserialize(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (Cache.TryGetValue(workItem, out var cached))
            return cached;

        if (workItem.Payload is not { } payload)
            return null;

        var parsed = payload.Deserialize<RuntimeCompleteActivityCommandPayload>();
        if (parsed is not null)
            Cache.AddOrUpdate(workItem, parsed);

        return parsed;
    }
}
