namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// Declares the default pipeline placement for a runtime middleware type so it travels with the middleware rather than
/// being restated at every registration site. A host can override the slot/order/name when it registers the middleware
/// (see the <c>Add*RuntimeMiddleware</c> extensions). Ordering is declarative: middleware target a stable named slot and
/// a coarse <see cref="Order"/> within it — never a concrete neighbouring middleware (ADR 0029).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeMiddlewareAttribute : Attribute
{
    public RuntimeMiddlewareAttribute(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        Slot = slot;
    }

    /// <summary>The stable named slot this middleware targets (e.g. a value from the pipeline's slot constants).</summary>
    public string Slot { get; }

    /// <summary>
    /// Coarse order within the slot. Built-in middleware sit at order 0; choose a negative order to run before the
    /// built-in in that slot or a positive order to run after it. Two distinct middleware sharing the same slot and
    /// order are a build-time error, so pick an explicit order rather than relying on registration sequence.
    /// </summary>
    public int Order { get; init; }

    /// <summary>Optional display name for the registration; defaults to the middleware type name.</summary>
    public string? Name { get; init; }
}
