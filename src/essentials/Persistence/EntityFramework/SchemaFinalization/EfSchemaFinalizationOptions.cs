using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The finalization gate's timings (spec 181, FR-005, FR-010 and FR-014; Decisions, Q12), bound from
/// <see cref="SectionName"/> in the configuration of the container a module's migrator runs in.
/// </summary>
public sealed class EfSchemaFinalizationOptions
{
    /// <summary>The section the timings are read from: <c>Elsa:Persistence:EntityFramework:Finalization</c>.</summary>
    public const string SectionName = "Elsa:Persistence:EntityFramework:Finalization";

    /// <summary>How often an active module evaluates its families when nothing else prompts it (FR-005): 30 seconds.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often an active module re-reads the finalized versions it writes (FR-010): 15 seconds.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long an activating module waits for an intent to finalize a version it cannot read to resolve, and for its
    /// readability report to be published, before it refuses (FR-013, FR-014): 2 minutes.
    /// </summary>
    public TimeSpan IntentWaitBound { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a waiting activation pauses between two reads of the record.</summary>
    public TimeSpan IntentPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Refuses a timing that would stop the gate from ever acting.</summary>
    public void Validate()
    {
        foreach (var (name, value) in new[]
                 {
                     (nameof(EvaluationInterval), EvaluationInterval),
                     (nameof(RefreshInterval), RefreshInterval),
                     (nameof(IntentWaitBound), IntentWaitBound),
                     (nameof(IntentPollInterval), IntentPollInterval)
                 })
        {
            if (value <= TimeSpan.Zero)
                throw new InvalidOperationException($"Configuration '{SectionName}:{name}' must be positive; it is {value}.");
        }
    }
}

/// <summary>
/// Reads <see cref="EfSchemaFinalizationOptions"/> from the container's own configuration, when it has one. A value
/// that is not a time span is refused rather than defaulted, as <see cref="EfMigrateOptions"/> refuses a policy it
/// does not know.
/// </summary>
internal sealed class EfSchemaFinalizationOptionsConfigurator(IServiceProvider services) : IConfigureOptions<EfSchemaFinalizationOptions>
{
    public void Configure(EfSchemaFinalizationOptions options)
    {
        if (EfSettingsSection.Of(services, EfSchemaFinalizationOptions.SectionName) is not { } section)
            return;
        options.EvaluationInterval = section.TimeSpan(nameof(options.EvaluationInterval)) ?? options.EvaluationInterval;
        options.RefreshInterval = section.TimeSpan(nameof(options.RefreshInterval)) ?? options.RefreshInterval;
        options.IntentWaitBound = section.TimeSpan(nameof(options.IntentWaitBound)) ?? options.IntentWaitBound;
        options.IntentPollInterval = section.TimeSpan(nameof(options.IntentPollInterval)) ?? options.IntentPollInterval;
        options.Validate();
    }
}
