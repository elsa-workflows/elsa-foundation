using Elsa.Primitives.Contracts;

namespace Elsa.Http.Tests.Support;

/// <summary>
/// Fixed-time <see cref="ISystemClock"/> for unit tests.
/// </summary>
public sealed class FakeSystemClock(DateTimeOffset? utcNow = null) : ISystemClock
{
    public DateTimeOffset UtcNow { get; } = utcNow ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
