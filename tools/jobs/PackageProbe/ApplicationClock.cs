using Microsoft.Extensions.DependencyInjection;

internal static partial class Probe
{
    internal static void ValidateApplicationClock(ProbeCommand command)
    {
        if (command.ApplicationClockOffsetHours is < -48 or > 48 ||
            command.ApplicationClockOffsetHours != 0 && command.Mode != "worker")
            throw new InvalidDataException("Application clock offsets are bounded worker-only qualification controls.");
    }

    internal static void ConfigureApplicationClock(ProbeCommand command, IServiceCollection services)
    {
        if (command.ApplicationClockOffsetHours != 0)
            services.AddSingleton<TimeProvider>(new ProbeApplicationClock(command.ApplicationClockOffsetHours));
    }

    internal static object ApplicationClockReady(ProbeCommand command, IServiceProvider services)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        return new
        {
            ready = true, processId = Environment.ProcessId, command.ApplicationClockOffsetHours,
            systemUtcNow = TimeProvider.System.GetUtcNow(), applicationUtcNow = clock.GetUtcNow(),
            timeProviderType = clock.GetType().FullName
        };
    }
}

// Qualification host only. Base TimeProvider timers, timestamps and timestamp frequency stay real;
// only the application wall clock differs. No global clock or persistence provider is replaced.
internal sealed class ProbeApplicationClock(int offsetHours) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddHours(offsetHours);
}
