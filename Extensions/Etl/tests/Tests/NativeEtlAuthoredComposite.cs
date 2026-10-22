using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using CompensationActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Etl.Tests;

/// <summary>Builds authored test content from the production CLR scanner and catalog factories.</summary>
internal sealed record NativeEtlAuthoredComposite(ActivityNode Root,
    IReadOnlyCollection<(ActivityDefinition Definition, ActivityDefinitionVersion Version)> SourceCatalog)
{
    public static NativeEtlAuthoredComposite Create(IServiceProvider services, ExecutableNode composite)
    {
        var nodes = composite.DescendantsAndSelf().ToArray();
        var keys = nodes.Select(node => node.ActivityType).ToHashSet(StringComparer.Ordinal);
        var catalog = CreateSourceCatalog(services, keys);
        var byType = catalog.ToDictionary(item => item.Definition.ActivityTypeKey, item => item.Version, StringComparer.Ordinal);
        var children = nodes.Where(node => node != composite).ToDictionary(node => node.ExecutableNodeId,
            node => Leaf(node, byType[node.ActivityType].Id), StringComparer.Ordinal);
        object authored = composite.ActivityType switch
        {
            var type when type == typeof(SequenceActivity).FullName => new SequenceAuthoredStructure(
                composite.Structure!.Payload.Deserialize<SequenceExecutableStructure>(JsonOptions)!.Activities.Select(id => children[id]).ToArray()),
            var type when type == typeof(FlowchartActivity).FullName => Flowchart(composite, children),
            var type when type == typeof(CompensationActivity).FullName => new CompensationScopeAuthoredStructure(
                composite.Structure!.Payload.Deserialize<CompensationScopeExecutableStructure>(JsonOptions)!.Steps
                    .Select(step => new CompensationScopeAuthoredStep(step.Name, children[step.Activity!])).ToArray()),
            _ => throw new InvalidOperationException("This fixture requires a supported real native composite.")
        };
        return new(new ActivityNode(composite.ExecutableNodeId, byType[composite.ActivityType].Id, [], [],
            new ActivityNodeStructure(composite.Structure!.Kind, composite.Structure.SchemaVersion,
                JsonSerializer.SerializeToElement(authored, authored.GetType(), JsonOptions))), catalog);
    }

    public static IReadOnlyCollection<(ActivityDefinition Definition, ActivityDefinitionVersion Version)> CreateSourceCatalog(
        IServiceProvider services,
        IReadOnlyCollection<string> typeKeys,
        string? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(typeKeys);
        var sourceIdentity = sourceId ?? AppContext.BaseDirectory;
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        var keys = typeKeys.ToHashSet(StringComparer.Ordinal);
        var scanned = services.GetRequiredService<IClrAssemblyScanner>().Scan(AppContext.BaseDirectory)
            .Where(model => keys.Contains(model.ActivityTypeKey)).ToArray();
        Assert.Equal(keys.Order(StringComparer.Ordinal), scanned.Select(model => model.ActivityTypeKey).Order(StringComparer.Ordinal));
        var definitions = services.GetRequiredService<IActivityDefinitionFactory>();
        var versions = services.GetRequiredService<IActivityDefinitionVersionFactory>();
        var serializer = services.GetRequiredService<IPayloadSerializer>();
        var catalog = scanned.Select(model =>
        {
            var definition = ActivityDefinition.From(definitions.Create(model.ActivityTypeKey, model.Category ?? string.Empty,
                model.DisplayName, model.Description, model.Id ?? ActivityCatalogStableIds.DefinitionId(model.ActivityTypeKey)));
            var descriptor = model.Descriptor is JsonElement element ? element : serializer.SerializeToElement(model.Descriptor);
            var version = ActivityDefinitionVersion.From(versions.Create(definition, model.Version, model.ProviderKey,
                model.ProviderSchemaVersion, model.ConsumerKey, model.ConsumerSchemaVersion, descriptor,
                "CLR", sourceIdentity, model.Inputs, model.Outputs, model.DesignFacets, model.ExecutionType,
                ActivityCatalogStableIds.VersionId(model.ActivityTypeKey, model.Version)));
            version.Definition = definition;
            return (Definition: definition, Version: version);
        }).ToArray();
        return catalog;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ActivityNode Leaf(ExecutableNode node, string versionId) => new(node.ExecutableNodeId, versionId,
        node.InputBindings.Values.Select(binding =>
        {
            Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
            return new ArgumentState(binding.InputKey, new(binding.LiteralValue!.Value.Clone(), "Literal"), null, null, null, null);
        }).ToArray(), []);

    private static FlowchartAuthoredStructure Flowchart(ExecutableNode node, IReadOnlyDictionary<string, ActivityNode> children)
    {
        var structure = node.Structure!.Payload.Deserialize<FlowchartStructure>(JsonOptions)!;
        return new(children.Values.ToArray(), structure.Connections, structure.StartNodeId,
            structure.NodeMetadata, structure.ConnectionMetadata, structure.Variables);
    }
}
