using Elsa.Primitives.Contracts;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Minimal <see cref="ISystemClock"/> double. The Groundwork design write commands stamp entity timestamps off
/// the host's clock; in production the host composes <c>PrimitivesFeature</c> to supply it. These composition
/// tests register this double the same way they register <see cref="FakePayloadSerializer"/> — a host primitive
/// the persistence feature consumes but does not own.
/// </summary>
internal sealed class FakeSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
