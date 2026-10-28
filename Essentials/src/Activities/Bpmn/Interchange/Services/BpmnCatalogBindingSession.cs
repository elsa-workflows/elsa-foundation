using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Models;
using Process = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>One import's catalog inventory and exact reference resolution, without writing authoring state.</summary>
internal sealed class BpmnCatalogBindingSession(IActivityDefinitionLookup catalog)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> Profile = new Dictionary<string, string>
    {
        [BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId] = "Elsa.Activities.Bpmn.Activities.BpmnProcess",
        [BpmnDocumentImporter.DefaultDelayActivityVersionId] = "Elsa.Activities.Scheduling.Activities.Delay",
        [BpmnDocumentImporter.DefaultEventActivityVersionId] = "Elsa.Activities.Primitives.Activities.Event",
        [BpmnDocumentImporter.DefaultPublishEventActivityVersionId] = "Elsa.Activities.Primitives.Activities.PublishEvent",
        [BpmnDocumentImporter.DefaultDispatchWorkflowActivityVersionId] = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow"
    };
    private readonly Dictionary<string, (string? VersionId, string Reason)> _references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _resolvedNodes = new(StringComparer.Ordinal);
    private readonly List<BpmnImportCapability> _missing = [];
    private IReadOnlyList<IActivityDefinition> _definitions = [];

    public static BpmnImportResult Unverified(BpmnImportResult result) => result with
    {
        Analysis = result.Analysis with
        {
            HostInventoryVerified = false,
            Capabilities = result.Analysis.Capabilities.Select(capability => capability.Availability == BpmnImportCapabilityAvailability.Available
                ? capability with { Availability = BpmnImportCapabilityAvailability.Unknown, Reason = "Proposed binding; the host activity catalog has not been verified." }
                : capability).ToArray()
        }
    };

    public async Task<BpmnImportResult> ResolveAsync(BpmnImportResult result, CancellationToken cancellationToken)
    {
        // ListDefinitions is the existing visible picker inventory: removed catalog rows do not qualify.
        _definitions = (await catalog.ListDefinitions(cancellationToken: cancellationToken)).ToArray();
        var processes = new List<BpmnImportedProcess>();
        foreach (var process in result.ProcessNodes)
            processes.Add(process with { Node = await ResolveNodeAsync(process.Node, cancellationToken) });
        var capabilities = result.Analysis.Capabilities.Select(capability =>
        {
            if (capability.Availability != BpmnImportCapabilityAvailability.Available)
                return capability;
            var available = capability.BindingRef is not null && _resolvedNodes.GetValueOrDefault(capability.BindingRef);
            return capability with
            {
                Availability = available ? BpmnImportCapabilityAvailability.Available : BpmnImportCapabilityAvailability.Unsupported,
                Reason = available ? "The required activity version is present in the visible host catalog." : "The required activity version is unavailable in the visible host catalog."
            };
        }).Concat(_missing).ToArray();
        return result with
        {
            ProcessNode = processes.Single(process => process.Node.NodeId == result.ProcessNode.NodeId).Node,
            ProcessNodes = processes,
            Analysis = result.Analysis with { HostInventoryVerified = true, Capabilities = capabilities }
        };
    }

    private async Task<ActivityNode> ResolveNodeAsync(ActivityNode node, CancellationToken cancellationToken)
    {
        var reference = await ReferenceAsync(node.ActivityVersionId, cancellationToken);
        _resolvedNodes[node.NodeId] = reference.VersionId is not null;
        if (reference.VersionId is null)
            _missing.Add(new BpmnImportCapability(node.NodeId, "ActivityReference", BpmnImportCapabilityAvailability.Unsupported, reference.Reason)
                { BindingRef = node.NodeId });
        var updated = node with { ActivityVersionId = reference.VersionId ?? node.ActivityVersionId };
        if (node.Structure?.Kind != Process.StructureKind)
            return updated;
        var source = ReadStructure(node);
        var children = new List<ActivityNode>();
        foreach (var child in source.Activities)
            children.Add(await ResolveNodeAsync(child, cancellationToken));
        var structure = new BpmnAuthoredStructure(children, source.Elements, source.SequenceFlows, source.Pools, source.Lanes,
            source.Variables, source.Diagram, source.IsTransaction, source.MessageFlows);
        return updated with { Structure = new(node.Structure.Kind, node.Structure.SchemaVersion, JsonSerializer.SerializeToElement(structure, Web)) };
    }

    private static BpmnAuthoredStructure ReadStructure(ActivityNode node)
    {
        if (node.Structure!.SchemaVersion != Process.StructureSchemaVersion)
            throw new BpmnInterchangeException($"BPMN binding node '{node.NodeId}' has unsupported authoring schema '{node.Structure.SchemaVersion}'.");
        try
        {
            return node.Structure.Payload.Deserialize<BpmnAuthoredStructure>(Web)
                ?? throw new BpmnInterchangeException($"BPMN binding node '{node.NodeId}' has a null authored structure.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new BpmnInterchangeException($"BPMN binding node '{node.NodeId}' has an invalid authored structure.", exception);
        }
    }

    private async Task<(string? VersionId, string Reason)> ReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        if (_references.TryGetValue(reference, out var cached))
            return cached;
        string? versionId = null;
        if (Profile.TryGetValue(reference, out var typeKey))
        {
            var definitions = _definitions.Where(definition => definition.ActivityTypeKey == typeKey).ToArray();
            if (definitions.Length == 1)
            {
                // This binding profile targets the existing 1.0.0 contracts; never choose a newer,
                // potentially incompatible native activity version merely because it sorts last.
                var recommended = await catalog.FindRecommendedVersion(definitions[0].Id, cancellationToken);
                if (recommended is not null && SemVer.TryParse(recommended.Version, out var version) &&
                    version.Major == 1 && version.Minor == 0 && version.Patch == 0 && version.Prerelease is null)
                    versionId = recommended.Id;
            }
        }
        else
        {
            var version = await catalog.FindVersion(reference, cancellationToken);
            if (version is not null && _definitions.Any(definition => definition.Id == version.DefinitionId))
                versionId = version.Id;
        }
        var result = (versionId, versionId is not null ? "Resolved" : $"Activity reference '{reference}' has no compatible recommended version in the visible catalog for this binding profile.");
        _references.Add(reference, result);
        return result;
    }
}
