using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.TriggerAuthoring;

[ShellFeature("AcceptanceTriggerAuthoring", DisplayName = "Acme trigger authoring")]
public sealed class Feature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStandaloneTriggerTypeSource, Types>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerConfigurationValidator, Validator>());
    }
}
internal sealed class Types : IStandaloneTriggerTypeSource
{
    public IEnumerable<TriggerTypeDescriptor> GetTypes() => [new("acceptance-event", 1, "Acme Event", "Event",
        [new("topic", "Topic", Required: true)], [], "Independent source consumer; authoring only, with no installed source intake.")
        { ConfigurationEditorId = "acme.standalone-editor" }];
}
internal sealed class Validator : ITriggerConfigurationValidator
{
    public string TypeKey => "acceptance-event";
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(schemaVersion == 1 && configuration.TryGetProperty("topic", out var topic) &&
            topic.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(topic.GetString()) ? [] : [new("InvalidTopic", "Enter a topic.", "topic")]);
    }
}
