using System.Collections.Frozen;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Services.Triggers.Management;

/// <summary>Composes authoring metadata and validation without enabling any runtime source or delivery pump.</summary>
public static class TriggerAuthoringRegistration
{
    public static void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<TriggerTypeCatalog>();
        services.TryAddSingleton<IStandaloneTriggerTypeCatalog>(provider => provider.GetRequiredService<TriggerTypeCatalog>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<StandaloneTriggerTypesInitializing>, CollectTriggerTypes>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<StandaloneTriggerConfigurationValidating>, ValidateTriggerConfiguration>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, InitializeTriggerTypes>());
    }
}

internal sealed class TriggerTypeCatalog : IStandaloneTriggerTypeCatalog
{
    private FrozenDictionary<string, TriggerTypeDescriptor>? types;
    internal void Initialize(IEnumerable<TriggerTypeDescriptor> source)
    {
        var entries = new Dictionary<string, TriggerTypeDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in source)
        {
            if (string.IsNullOrWhiteSpace(descriptor.TypeKey) || descriptor.SchemaVersion <= 0 ||
                string.IsNullOrWhiteSpace(descriptor.DisplayName) || string.IsNullOrWhiteSpace(descriptor.Family) ||
                descriptor.ConfigurationFields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != descriptor.ConfigurationFields.Count)
                throw new InvalidOperationException("A standalone trigger type has invalid authoring metadata.");
            if (!entries.TryAdd(descriptor.TypeKey, Detach(descriptor)))
                throw new InvalidOperationException($"Standalone trigger type '{descriptor.TypeKey}' has multiple owners.");
        }
        if (Interlocked.CompareExchange(ref types, entries.ToFrozenDictionary(StringComparer.Ordinal), null) is not null)
            throw new InvalidOperationException("Standalone trigger types have already completed initialization.");
    }
    public IReadOnlyList<TriggerTypeDescriptor> List() => Entries.Values.OrderBy(type => type.DisplayName, StringComparer.Ordinal).ToArray();
    public TriggerTypeDescriptor? Find(string typeKey) => Entries.GetValueOrDefault(typeKey);
    private FrozenDictionary<string, TriggerTypeDescriptor> Entries => types ?? throw new TriggerOperationException("unavailable", "Trigger authoring metadata has not completed initialization.");

    internal static TriggerTypeDescriptor Detach(TriggerTypeDescriptor descriptor) => descriptor with
    {
        ConfigurationFields = Array.AsReadOnly(descriptor.ConfigurationFields.Select(field => field with
        {
            DefaultValue = field.DefaultValue?.Clone(),
            Choices = field.Choices is null ? null : Array.AsReadOnly(field.Choices.ToArray())
        }).ToArray()),
        SourceFields = Array.AsReadOnly(descriptor.SourceFields.Select(field => field with
        {
            Path = Array.AsReadOnly(field.Path.ToArray())
        }).ToArray())
    };
}

internal sealed class CollectTriggerTypes(IEnumerable<IStandaloneTriggerTypeSource> sources) : IEventHandler<StandaloneTriggerTypesInitializing>
{
    public Task Handle(StandaloneTriggerTypesInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var type in source.GetTypes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Snapshot before MoveNext can dispose a contributor's JSON document or mutate its collections.
                domainEvent.Types.Add(TriggerTypeCatalog.Detach(type));
            }
        return Task.CompletedTask;
    }
}

internal sealed class ValidateTriggerConfiguration(IEnumerable<ITriggerConfigurationValidator> validators)
    : IEventHandler<StandaloneTriggerConfigurationValidating>
{
    public async Task Handle(StandaloneTriggerConfigurationValidating domainEvent, CancellationToken cancellationToken)
    {
        var owners = validators.Where(validator => validator.TypeKey == domainEvent.TypeKey).ToArray();
        if (owners.Length != 1)
            throw new TriggerOperationException("unavailable", "The selected trigger type requires exactly one configuration validator.");
        foreach (var finding in await owners[0].ValidateAsync(domainEvent.SchemaVersion, domainEvent.Configuration, cancellationToken))
            domainEvent.Findings.Add(finding);
    }
}

internal sealed class InitializeTriggerTypes(IInlineEventPublisher publisher, TriggerTypeCatalog catalog,
    IEnumerable<ITriggerConfigurationValidator> validators) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var collecting = new StandaloneTriggerTypesInitializing();
        await publisher.Publish(collecting, cancellationToken);
        var owners = validators.ToArray();
        foreach (var type in collecting.Types)
            if (owners.Count(validator => validator.TypeKey == type.TypeKey) != 1)
                throw new InvalidOperationException($"Standalone trigger type '{type.TypeKey}' requires exactly one scoped configuration validator.");
        if (owners.Any(validator => collecting.Types.All(type => type.TypeKey != validator.TypeKey)))
            throw new InvalidOperationException("A standalone trigger validator has no authoring type descriptor.");
        catalog.Initialize(collecting.Types);
    }
}
