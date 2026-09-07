using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>Default provider-neutral runtime requirement checker.</summary>
public sealed class RuntimeRequirementChecker(
    IEnumerable<IRuntimeActivityConsumerCapability> activityConsumers,
    IRuntimeDurableValueStorageDriverRegistry storageDrivers,
    IWellKnownTypeRegistry typeRegistry,
    IPayloadSerializer payloadSerializer) : IRuntimeRequirementChecker
{
    private const string ClrConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity;

    public RuntimeRequirementCheckResult Check(RuntimeRequirementCheckSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new(
            subject.ArtifactId,
            CheckConsumers(subject.RuntimeRequirements),
            CheckStorageDrivers(subject.StorageDriverRequirements),
            CheckActivityTypes(subject.Nodes));
    }

    private IReadOnlyList<RuntimeRequirementStatusEntry> CheckConsumers(
        IReadOnlyCollection<RuntimeRequirement> requirements)
    {
        var consumersByKey = activityConsumers
            .GroupBy(capability => capability.ConsumerKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(capability => capability.SupportedSchemaVersions)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return requirements
            .Distinct()
            .OrderBy(requirement => requirement.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.SchemaVersion, StringComparer.Ordinal)
            .Select(requirement =>
            {
                IReadOnlyCollection<string> supported = consumersByKey.GetValueOrDefault(requirement.ConsumerKey) ?? [];
                var status = supported.Contains(requirement.SchemaVersion, StringComparer.Ordinal)
                    ? RuntimeRequirementStatus.Available
                    : supported.Count == 0
                        ? RuntimeRequirementStatus.Missing
                        : RuntimeRequirementStatus.UnsupportedSchema;
                return new RuntimeRequirementStatusEntry(
                    requirement.ConsumerKey,
                    requirement.SchemaVersion,
                    status,
                    supported.Order(StringComparer.Ordinal).ToArray());
            })
            .ToArray();
    }

    private IReadOnlyList<StorageDriverStatusEntry> CheckStorageDrivers(
        IReadOnlyCollection<RuntimeStorageDriverRequirement> requirements) =>
        requirements
            .Distinct()
            .OrderBy(requirement => requirement.DriverKey, StringComparer.Ordinal)
            .Select(requirement => new StorageDriverStatusEntry(
                requirement.DriverKey,
                storageDrivers.Contains(requirement.DriverKey)
                    ? RuntimeRequirementStatus.Available
                    : RuntimeRequirementStatus.Missing))
            .ToArray();

    private IReadOnlyList<ActivityTypeStatusEntry> CheckActivityTypes(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodesByAlias = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unreadable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node.IntrinsicKind is not null ||
                !StringComparer.Ordinal.Equals(node.DescriptorType, ClrConsumerKey))
                continue;

            if (!TryReadTypeAlias(node.DescriptorPayload, out var alias))
            {
                unreadable.Add(node.ExecutableNodeId);
                continue;
            }

            if (!nodesByAlias.TryGetValue(alias, out var nodeIds))
                nodesByAlias[alias] = nodeIds = [];
            nodeIds.Add(node.ExecutableNodeId);
        }

        var entries = nodesByAlias
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ActivityTypeStatusEntry(
                pair.Key,
                pair.Value.Order(StringComparer.Ordinal).ToArray(),
                typeRegistry.TryGetTypeOrDefault(pair.Key, out _)
                    ? RuntimeRequirementStatus.Available
                    : RuntimeRequirementStatus.MissingActivityType))
            .ToList();

        if (unreadable.Count > 0)
            entries.Add(new(
                string.Empty,
                unreadable.Order(StringComparer.Ordinal).ToArray(),
                RuntimeRequirementStatus.MissingActivityType));

        return entries;
    }

    private bool TryReadTypeAlias(System.Text.Json.JsonElement payload, out string alias)
    {
        alias = string.Empty;

        try
        {
            var value = payloadSerializer.Deserialize<ClrActivityDescriptor>(payload).TypeAlias;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            alias = value;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
