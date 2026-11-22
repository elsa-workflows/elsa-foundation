using System.Text.Json;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Releases;

internal sealed class PrimitiveReleaseOccurrenceInspector(IReleaseRuntimeSiteReader sites) : IReleaseOccurrenceInspector
{
    private readonly ConditionalWeakTable<IReleaseContentReader, ConcurrentDictionary<ReleaseMemberKey, Lazy<MemberIndex>>> _members = new();
    private static readonly string WriteLineAlias = TypeAliasConvention.CanonicalAlias(typeof(WriteLine));
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.primitive-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ClrActivity, WriteLineAlias, 1)
    });

    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) return ValueTask.FromResult(Failed("primitive_activity_unsupported", ReleaseSemanticStatus.Unsupported));
            var context = sites.ReadNode(site, content); var node = context.Node;
            if (site.Input is not null || node.IntrinsicKind is not null || node.ChildSlots.Count != 0 || node.Structure is not null || node.OutputCaptures.Count != 0 ||
                node.ExecutionPlacement != ActivityExecutionPlacement.Inline || node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.ClrActivity ||
                node.Metadata.ContainsKey(RuntimeMetadataKeys.StructuralFaultHandling) || node.Metadata.ContainsKey(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild) ||
                node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) || node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion) ||
                node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) && executionType != "Action")
                return ValueTask.FromResult(Failed("primitive_activity_shape_mismatch"));
            var index = Index(context, content);
            if (index.DependencyNodes.Contains(node.ExecutableNodeId)) return ValueTask.FromResult(Failed("primitive_dependency_inventory_mismatch"));
            if (index.ByNode.ContainsKey(node.ExecutableNodeId)) return ValueTask.FromResult(Failed("primitive_resume_target_mismatch"));
            var contract = node.ActivityContract;
            if (contract is null) return ValueTask.FromResult(Failed("primitive_contract_unsupported", ReleaseSemanticStatus.Unsupported));
            var alias = site.Site.Discriminator; const string inputKey = "text";
            if (contract.ActivityTypeKey != node.ActivityType || contract.ContractVersion != node.ActivityTypeVersion ||
                contract.DescriptorKind != typeof(ClrActivityDescriptor).FullName || contract.Activation.DescriptorKind != contract.DescriptorKind ||
                contract.Activation.Capability != alias || !JsonElement.DeepEquals(contract.DescriptorPayload, node.DescriptorPayload) ||
                contract.SideEffectProfile != SideEffectProfile.ReplaySafe || contract.SupportsBackgroundExecution ||
                contract.Result.Type != new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))) ||
                !contract.Result.IsRequired || contract.Result.Projections.Count != 0 || !contract.Outcomes.ToHashSet(StringComparer.Ordinal).SetEquals(["Done"]) ||
                contract.Inputs.Count != 1 || !contract.Inputs.TryGetValue(inputKey, out var input) ||
                input.Name != nameof(WriteLine.Text) || !input.IsRequired || input.IsNullable ||
                input.Type != new ValueTypeDescriptor("String", CollectionKind.Single) || input.IsCredential || input.ConnectionType is not null || input.IsStructureInput ||
                node.InputBindings.Keys.Any(key => key != inputKey))
                return ValueTask.FromResult(Failed("primitive_contract_mismatch"));
            if (context.Workflow is not null && input.IsRequired && !input.HasDefault && !node.InputBindings.ContainsKey(inputKey))
                return ValueTask.FromResult(Failed("primitive_required_input_missing"));
            if (input.HasDefault && !Text(input.DefaultValue!.Value, input.IsNullable)) return ValueTask.FromResult(Failed("primitive_default_mismatch"));
            if (node.InputBindings.TryGetValue(inputKey, out var binding))
            {
                if (binding.TargetType != input.Type) return ValueTask.FromResult(Failed("primitive_binding_type_mismatch"));
                if (binding.Source == RuntimeInputBindingSource.SecretRead || binding.Literal?.WithheldValue?.Kind == WithheldValueKind.SecretReference)
                    return ValueTask.FromResult(Failed("primitive_secret_binding_refused"));
                if (binding.ConversionPlan is { } conversion && conversion.Operation is not (ValueConversionOperation.Identity or ValueConversionOperation.NullableCompatibility))
                    return ValueTask.FromResult(Failed("primitive_conversion_unsupported", ReleaseSemanticStatus.Unsupported));
                if (binding.ConversionPlan is { } plan && (!ValueConversionCompatibility.SameType(plan.TargetType, input.Type) ||
                    plan.Operation == ValueConversionOperation.Identity && !ValueConversionCompatibility.SameType(plan.SourceType, plan.TargetType) ||
                    plan.Operation == ValueConversionOperation.NullableCompatibility && !ValueConversionCompatibility.IsNullableCompatibility(plan.SourceType, plan.TargetType)))
                    return ValueTask.FromResult(Failed("primitive_conversion_mismatch"));
                if (binding.Source == RuntimeInputBindingSource.Literal && binding.LiteralValue is null) return ValueTask.FromResult(Failed("primitive_literal_missing"));
                if (binding.LiteralValue is { } literal && !Text(literal, input.IsNullable)) return ValueTask.FromResult(Failed("primitive_literal_mismatch"));
            }
            var key = JsonSerializer.Serialize(new object?[] { site.Member.Kind, site.Member.Identity, site.Origin, site.Node, "activity-module", "ActivitiesPrimitives" });
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified, prerequisites:
                [new(key, site.Member, "activity-module", "ActivitiesPrimitives", "opaque", "required")]));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ReleaseRuntimeArtifactException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_primitive_captured_content")); }

        ReleaseOccurrenceFacts Failed(string code, ReleaseSemanticStatus status = ReleaseSemanticStatus.Invalid) => new(status, diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }

    private static bool Text(JsonElement value, bool nullable) => value.ValueKind == JsonValueKind.Null ? nullable : value.ValueKind == JsonValueKind.String;

    private MemberIndex Index(ReleaseRuntimeNodeContext context, IReleaseContentReader content) => _members.GetValue(content, _ => new())
        .GetOrAdd(context.Member.Key, _ => new(() => new(context.Workflow?.ResumeTargets ?? context.Template!.ResumeTargets, context.Workflow?.Dependencies ?? []), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    private sealed class MemberIndex(IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> targets, IReadOnlyCollection<WorkflowExecutableDependency> dependencies)
    {
        public HashSet<string> DependencyNodes { get; } = dependencies.SelectMany(dependency => dependency.DispatchNodeIds).ToHashSet(StringComparer.Ordinal);
        public Dictionary<string, KeyValuePair<string, WorkflowExecutableResumeTarget>[]> ByNode { get; } = targets
            .GroupBy(pair => pair.Value.ExecutableNodeId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }
}
