using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The post-finalization backfill's settings (spec 186, FR-008, FR-009, FR-012, FR-018 and FR-023), bound from
/// <see cref="SectionName"/> in the configuration of the container a module's migrator runs in.
/// </summary>
public sealed class EfSchemaBackfillOptions
{
    /// <summary>The section the settings are read from: <c>Elsa:Persistence:EntityFramework:Backfill</c>.</summary>
    public const string SectionName = "Elsa:Persistence:EntityFramework:Backfill";

    /// <summary>How many rows one batch selects and rewrites (FR-009): 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>How long the backfill pauses between two batches, so a long run does not starve live traffic (FR-009).</summary>
    public TimeSpan BatchPause { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often the backfill looks, from what the finalization gate last observed and with no database round trip,
    /// whether a family has work: 15 seconds, the gate's refresh interval.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often a family whose completion stands is audited for rows below it (FR-018): one hour. A family blocked by
    /// anything but rows an operator repairs in place, content-addressed rows among them, is surveyed again at the same
    /// interval (FR-023).
    /// </summary>
    public TimeSpan AuditInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often a family blocked by rows an operator repairs in place, with a stamp this host cannot read or that fail to
    /// upcast, is surveyed again (FR-006, FR-023): 5 minutes, so a repair is seen well before the next audit. A family
    /// blocked by those and by others too is surveyed at this interval.
    /// </summary>
    public TimeSpan RepairableBlockerInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the settle condition must have held before the verification pass starts (FR-012), or null for the
    /// membership expiry period plus the skew allowance, which the fleet reports.
    /// </summary>
    public TimeSpan? SettleMargin { get; set; }

    /// <summary>
    /// How long a worker's claim on a family's run holds without renewal (FR-008): 2 minutes. Zero keeps no claim, so every
    /// worker runs; nothing correct depends on the claim.
    /// </summary>
    public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How many verification passes one run makes before it leaves the family to the next round, when each pass finds
    /// and rewrites a row the one before it had not (FR-013): 3.
    /// </summary>
    public int VerificationPasses { get; set; } = 3;

    /// <summary>Refuses a setting that would stop the backfill from ever finishing, or from ever pausing.</summary>
    public void Validate()
    {
        if (BatchSize <= 0)
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(BatchSize)}' must be positive; it is {BatchSize}.");
        if (VerificationPasses <= 0)
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(VerificationPasses)}' must be positive; it is {VerificationPasses}.");
        foreach (var (name, value) in new[] { (nameof(CheckInterval), CheckInterval), (nameof(AuditInterval), AuditInterval), (nameof(RepairableBlockerInterval), RepairableBlockerInterval) })
        {
            if (value <= TimeSpan.Zero)
                throw new InvalidOperationException($"Configuration '{SectionName}:{name}' must be positive; it is {value}.");
        }

        foreach (var (name, value) in new[] { (nameof(BatchPause), BatchPause), (nameof(ClaimDuration), ClaimDuration), (nameof(SettleMargin), SettleMargin ?? TimeSpan.Zero) })
        {
            if (value < TimeSpan.Zero)
                throw new InvalidOperationException($"Configuration '{SectionName}:{name}' must not be negative; it is {value}.");
        }
    }
}

/// <summary>
/// Reads <see cref="EfSchemaBackfillOptions"/> from the container's own configuration, when it has one. A value that is
/// not a number or a time span is refused rather than defaulted.
/// </summary>
internal sealed class EfSchemaBackfillOptionsConfigurator(IServiceProvider services) : IConfigureOptions<EfSchemaBackfillOptions>
{
    public void Configure(EfSchemaBackfillOptions options)
    {
        if (EfSettingsSection.Of(services, EfSchemaBackfillOptions.SectionName) is not { } section)
            return;
        options.BatchSize = section.WholeNumber(nameof(options.BatchSize)) ?? options.BatchSize;
        options.VerificationPasses = section.WholeNumber(nameof(options.VerificationPasses)) ?? options.VerificationPasses;
        options.BatchPause = section.TimeSpan(nameof(options.BatchPause)) ?? options.BatchPause;
        options.CheckInterval = section.TimeSpan(nameof(options.CheckInterval)) ?? options.CheckInterval;
        options.AuditInterval = section.TimeSpan(nameof(options.AuditInterval)) ?? options.AuditInterval;
        options.RepairableBlockerInterval = section.TimeSpan(nameof(options.RepairableBlockerInterval)) ?? options.RepairableBlockerInterval;
        options.SettleMargin = section.TimeSpan(nameof(options.SettleMargin)) ?? options.SettleMargin;
        options.ClaimDuration = section.TimeSpan(nameof(options.ClaimDuration)) ?? options.ClaimDuration;
        options.Validate();
    }
}
