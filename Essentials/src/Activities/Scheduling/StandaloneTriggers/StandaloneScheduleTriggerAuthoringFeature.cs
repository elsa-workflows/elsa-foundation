using System.Globalization;
using System.Text.Json;
using System.Xml;
using Cronos;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling.StandaloneTriggers;

/// <summary>Timer/Cron authoring metadata and pure validation. Installs no scheduler, pump or runtime listener.</summary>
[ShellFeature(name: "StandaloneScheduleTriggerAuthoring", DisplayName = "Schedule trigger authoring", Description = "Describe independent Timer and Cron entry points.")]
public class StandaloneScheduleTriggerAuthoringFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStandaloneTriggerTypeSource, ScheduleTriggerTypeSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerConfigurationValidator, TimerTriggerConfigurationValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerConfigurationValidator, CronTriggerConfigurationValidator>());
    }
}

internal sealed class ScheduleTriggerTypeSource : IStandaloneTriggerTypeSource
{
    public IEnumerable<TriggerTypeDescriptor> GetTypes()
    {
        TriggerSourceField[] source = [new(["metadata", "dueAt"], "Due instant (UTC)", "string"), new(["metadata", "timezone"], "Schedule timezone", "string")];
        yield return new("timer", 1, "Timer", "Timer", [new("interval", "Interval", Required: true, DefaultValue: JsonSerializer.SerializeToElement("PT5M"))], source,
            "One resource-owned recurring interval; accepted subscribers start independently.");
        yield return new("cron", 1, "Cron", "Cron", [new("expression", "Cron expression", Required: true),
            new("timezone", "Timezone", Required: true, DefaultValue: JsonSerializer.SerializeToElement("UTC"))], source,
            "Five or six Cron fields with an explicit timezone.");
    }
}

internal sealed class TimerTriggerConfigurationValidator : ITriggerConfigurationValidator
{
    public string TypeKey => "timer";
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var findings = new List<TriggerFinding>();
        if (schemaVersion != 1) findings.Add(new("UnsupportedSchema", "The Timer configuration version is unsupported."));
        try
        {
            var expression = configuration.TryGetProperty("interval", out var interval) && interval.ValueKind == JsonValueKind.String ? interval.GetString() : null;
            if (string.IsNullOrWhiteSpace(expression)) throw new FormatException();
            var duration = expression.StartsWith('P') || expression.StartsWith("-P", StringComparison.Ordinal)
                ? XmlConvert.ToTimeSpan(expression) : TimeSpan.Parse(expression, CultureInfo.InvariantCulture);
            if (duration <= TimeSpan.Zero) throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        { findings.Add(new("InvalidInterval", "Enter a positive ISO-8601 duration or invariant time interval.", "interval")); }
        if (configuration.TryGetProperty("http", out _)) findings.Add(new("UnexpectedHttpMode", "HTTP response options do not apply to schedules.", "http"));
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(findings);
    }
}

internal sealed class CronTriggerConfigurationValidator : ITriggerConfigurationValidator
{
    public string TypeKey => "cron";
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var findings = new List<TriggerFinding>();
        if (schemaVersion != 1) findings.Add(new("UnsupportedSchema", "The Cron configuration version is unsupported."));
        var expression = configuration.TryGetProperty("expression", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        try
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new FormatException();
            var count = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (count is not (5 or 6)) throw new FormatException();
            _ = CronExpression.Parse(expression, count == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
        }
        catch (Exception exception) when (exception is CronFormatException or FormatException)
        { findings.Add(new("InvalidCron", "Enter a valid five-field or six-field Cron expression.", "expression")); }
        try
        {
            var zone = configuration.TryGetProperty("timezone", out var timezone) && timezone.ValueKind == JsonValueKind.String ? timezone.GetString() : "UTC";
            if (string.IsNullOrWhiteSpace(zone)) throw new TimeZoneNotFoundException();
            _ = TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        { findings.Add(new("InvalidTimezone", "Choose an installed timezone.", "timezone")); }
        if (configuration.TryGetProperty("http", out _)) findings.Add(new("UnexpectedHttpMode", "HTTP response options do not apply to schedules.", "http"));
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(findings);
    }
}
