using Microsoft.Extensions.Time.Testing;

namespace Elsa.Jobs.Tests;

/// <summary>Observes timer installation while delegating every clock/timer operation to FakeTimeProvider.</summary>
internal sealed class ObservingTimeProvider(FakeTimeProvider inner, Action<TimeSpan> observe) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = inner.CreateTimer(callback, state, dueTime, period);
        observe(dueTime);
        return timer;
    }
}
