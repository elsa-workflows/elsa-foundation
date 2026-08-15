using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Default <see cref="IRuntimeRequirementChecker"/>.
/// </summary>
/// <remarks>
/// <para>
/// The capability and storage-driver evaluation is relocated verbatim from the publishing
/// deployment preflight: consumer schema is exact ordinal set-membership over the advertised
/// supported-schema list, and driver keys are exact, unversioned containment. The extraction
/// relocates the check without redefining its semantics (FR-B-005), so a behavioural difference
/// here is a defect, not an improvement.
/// </para>
/// <para>
/// <paramref name="typeRegistry"/> is <b>required</b>, deliberately breaking with the nullable
/// pattern used by <c>WorkflowExecutableInputValidator</c>, <c>RuntimeValueConversionExecutor</c>
/// and <c>RuntimeActivityInputMaterializer</c>. Those services <em>degrade meaningfully</em> without
/// a registry — a conversion falls back, validation gets lenient. This one does not: it is a
/// <b>gate</b>, and a gate without its registry does not degrade, it simply stops gating. An
/// artifact would then pass import and throw <c>UnknownActivityTypeException</c> at first activation
/// in production, which is the exact failure US2 exists to prevent, and it would be near-impossible
/// to trace back to a missing registration. A required dependency makes an unsupported composition
/// fail loudly at startup naming what is absent, instead of making two very different states look
/// equally supported.
/// </para>
/// </remarks>
public sealed class RuntimeRequirementChecker(
    IEnumerable<IRuntimeActivityConsumerCapability> activityConsumers,
    IRuntimeDurableValueStorageDriverRegistry storageDrivers,
    IWellKnownTypeRegistry typeRegistry) : IRuntimeRequirementChecker
{
    private static readonly string ClrDescriptorKind = typeof(ClrActivityDescriptor).FullName!;

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

    /// <summary>
    /// Second axis: per-node CLR activity-type presence, using the same predicate the CLR activator
    /// applies at activation time (<c>IWellKnownTypeRegistry.TryGetTypeOrDefault</c> over
    /// <c>ClrActivityDescriptor.TypeAlias</c>). Checking it here is what turns a first-activation
    /// <c>UnknownActivityTypeException</c> into an import-time rejection (US2).
    /// </summary>
    /// <remarks>
    /// Engine intrinsics and non-CLR descriptor kinds carry no CLR activity type and are skipped —
    /// they are resolved by the engine, not through the type registry. A descriptor payload that
    /// cannot be read as a <see cref="ClrActivityDescriptor"/> is reported as a missing type rather
    /// than thrown, so a malformed node yields a diagnostic instead of aborting the whole check.
    /// </remarks>
    private IReadOnlyList<ActivityTypeStatusEntry> CheckActivityTypes(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodesByAlias = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unreadable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node.IntrinsicKind is not null)
                continue;
            if (!StringComparer.Ordinal.Equals(node.DescriptorType, ClrDescriptorKind))
                continue;

            if (!TryReadTypeAlias(node.DescriptorPayload, out var alias))
            {
                // An unreadable CLR descriptor cannot be resolved at activation either; surface it
                // against the node rather than letting a serialization failure escape the gate.
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

    /// <summary>
    /// Reads <see cref="ClrActivityDescriptor.TypeAlias"/> straight off the descriptor payload.
    /// </summary>
    /// <remarks>
    /// Reading the single property directly keeps this check free of an <c>IPayloadSerializer</c>
    /// dependency, which would otherwise be a second optional collaborator to reason about. The
    /// lookup is case-insensitive because the alias survives whichever naming policy serialized it.
    /// </remarks>
    private static bool TryReadTypeAlias(JsonElement payload, out string alias)
    {
        alias = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in payload.EnumerateObject())
        {
            if (!property.NameEquals(nameof(ClrActivityDescriptor.TypeAlias)) &&
                !StringComparer.OrdinalIgnoreCase.Equals(property.Name, nameof(ClrActivityDescriptor.TypeAlias)))
                continue;
            if (property.Value.ValueKind != JsonValueKind.String)
                return false;

            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            alias = value;
            return true;
        }

        return false;
    }
}
