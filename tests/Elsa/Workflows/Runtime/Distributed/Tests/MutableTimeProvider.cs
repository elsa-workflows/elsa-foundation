namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// A controllable <see cref="TimeProvider"/> for deterministic tests: time only moves when the test calls
/// <see cref="Advance"/> or <see cref="SetUtcNow"/>. No wall-clock reads and no sleeps, so placement/transport
/// expiry and the two-node kill test are fully deterministic.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;

    public void SetUtcNow(DateTimeOffset now) => _now = now;
}
