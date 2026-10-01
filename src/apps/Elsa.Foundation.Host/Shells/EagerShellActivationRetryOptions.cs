namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// How long <see cref="EagerShellActivationHostedService"/> waits between attempts to activate a shell that failed: the delay
/// doubles from <see cref="InitialDelay"/> after each failed attempt and stops growing at <see cref="MaxDelay"/>, which is the
/// interval the host keeps trying at for as long as the shell stays down.
/// </summary>
/// <remarks>
/// <c>Elsa:Boot:EagerShellActivation:Retry:InitialDelay</c> and <c>MaxDelay</c>, as <c>TimeSpan</c>s. A value that is absent,
/// unparseable or not positive is the default, and a <c>MaxDelay</c> below <c>InitialDelay</c> is raised to it.
/// </remarks>
public sealed record EagerShellActivationRetryOptions
{
    public const string SectionKey = "Elsa:Boot:EagerShellActivation:Retry";

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);

    public static EagerShellActivationRetryOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionKey);
        var defaults = new EagerShellActivationRetryOptions();
        var initial = Positive(section[nameof(InitialDelay)]) ?? defaults.InitialDelay;
        var max = Positive(section[nameof(MaxDelay)]) ?? defaults.MaxDelay;
        return new() { InitialDelay = initial, MaxDelay = max < initial ? initial : max };
    }

    /// <summary>
    /// The wait after <paramref name="failedAttempts"/> failures in a row. An EF module's refusal waits the whole
    /// <see cref="MaxDelay"/> from the first: a retry cannot change what an operator has to resolve, so the host only checks
    /// back, at the interval it keeps for a shell that stays down.
    /// </summary>
    public TimeSpan DelayAfter(int failedAttempts, bool refused)
    {
        if (refused)
            return MaxDelay;

        // The doubling stops long before the shift could overflow: the cap is reached within a few dozen failures at most.
        var doublings = Math.Min(failedAttempts - 1, 30);
        var delay = InitialDelay.TotalMilliseconds * (1L << doublings);
        return delay >= MaxDelay.TotalMilliseconds ? MaxDelay : TimeSpan.FromMilliseconds(delay);
    }

    private static TimeSpan? Positive(string? value) =>
        TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero ? parsed : null;
}
