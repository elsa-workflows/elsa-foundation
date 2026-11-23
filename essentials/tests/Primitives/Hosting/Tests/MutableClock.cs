using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Hosting.Tests;

/// <summary>
/// A test clock whose time can be advanced deterministically.
/// </summary>
internal sealed class MutableClock(DateTimeOffset start) : ISystemClock
{
    public MutableClock() : this(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
