using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Primitives.StandaloneTriggers;

/// <summary>Broadcast Event authoring metadata, independently composable from activity execution and delivery.</summary>
[ShellFeature(name: "StandaloneEventTriggerAuthoring", DisplayName = "Event trigger authoring", Description = "Describe independent broadcast Event entry points.")]
public class StandaloneEventTriggerAuthoringFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStandaloneTriggerTypeSource, EventTriggerTypeSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerConfigurationValidator, EventTriggerConfigurationValidator>());
    }
}

internal sealed class EventTriggerTypeSource : IStandaloneTriggerTypeSource
{
    public IEnumerable<TriggerTypeDescriptor> GetTypes()
    {
        yield return new("event", 1, "Event", "Event", [new("eventName", "Event name", Required: true)],
            [new(["payload"], "Event payload", "object"), new(["metadata", "eventName"], "Event name", "string")],
            "Broadcast to independently eligible subscriptions.");
    }
}

internal sealed class EventTriggerConfigurationValidator : ITriggerConfigurationValidator
{
    public string TypeKey => "event";
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<TriggerFinding>();
        if (schemaVersion != 1) findings.Add(new("UnsupportedSchema", "The Event configuration version is unsupported."));
        if (!configuration.TryGetProperty("eventName", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
            name.GetString()!.Length > 256 || name.GetString()!.Any(char.IsControl))
            findings.Add(new("InvalidEventName", "Enter a nonempty Event name of at most 256 characters.", "eventName"));
        if (configuration.TryGetProperty("http", out _)) findings.Add(new("UnexpectedHttpMode", "HTTP response options do not apply to Event triggers.", "http"));
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(findings);
    }
}
