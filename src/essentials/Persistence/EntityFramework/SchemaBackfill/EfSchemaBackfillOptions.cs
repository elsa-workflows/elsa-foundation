using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The post-finalization backfill's settings (spec 186, FR-008, FR-009, FR-012 and FR-018), bound from
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

    /// <summary>How often a family whose completion stands is audited for rows below it (FR-018): one hour.</summary>
    public TimeSpan AuditInterval { get; set; } = TimeSpan.FromHours(1);

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
        foreach (var (name, value) in new[] { (nameof(CheckInterval), CheckInterval), (nameof(AuditInterval), AuditInterval) })
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
        var section = services.GetService<IConfiguration>()?.GetSection(EfSchemaBackfillOptions.SectionName);
        if (section is null)
            return;
        options.BatchSize = ReadNumber(section, nameof(options.BatchSize)) ?? options.BatchSize;
        options.VerificationPasses = ReadNumber(section, nameof(options.VerificationPasses)) ?? options.VerificationPasses;
        options.BatchPause = ReadSpan(section, nameof(options.BatchPause)) ?? options.BatchPause;
        options.CheckInterval = ReadSpan(section, nameof(options.CheckInterval)) ?? options.CheckInterval;
        options.AuditInterval = ReadSpan(section, nameof(options.AuditInterval)) ?? options.AuditInterval;
        options.SettleMargin = ReadSpan(section, nameof(options.SettleMargin)) ?? options.SettleMargin;
        options.ClaimDuration = ReadSpan(section, nameof(options.ClaimDuration)) ?? options.ClaimDuration;
        options.Validate();
    }

    private static int? ReadNumber(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Configuration '{EfSchemaBackfillOptions.SectionName}:{key}' is '{value}', which is not a whole number.");
    }

    private static TimeSpan? ReadSpan(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Configuration '{EfSchemaBackfillOptions.SectionName}:{key}' is '{value}', which is not a time span such as '00:00:30'.");
    }
}
