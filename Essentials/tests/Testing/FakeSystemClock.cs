using Elsa.Primitives.Contracts;

namespace Elsa.Testing;

/// <summary>
/// Fixed <see cref="ISystemClock"/> that only moves when a test calls <see cref="Advance"/>.
/// </summary>
public sealed class FakeSystemClock(DateTimeOffset? utcNow = null) : ISystemClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
