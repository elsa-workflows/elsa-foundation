using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Internal;

internal sealed class DelayReleaseOccurrenceInspector(IReleaseRuntimeSiteReader sites) : IReleaseOccurrenceInspector
{
    private readonly ConditionalWeakTable<IReleaseContentReader, ConcurrentDictionary<ReleaseMemberKey, Lazy<ResumeIndex>>> _resumes = new();
    private const string LocalResumeId = "resume-target:durable-timer";
    private static readonly string Alias = TypeAliasConvention.CanonicalAlias(typeof(Delay));
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.delay-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[] { new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ClrActivity, Alias, 1) });

    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) return ValueTask.FromResult(Failed("scheduling_activity_unsupported", ReleaseSemanticStatus.Unsupported));
            var context = sites.ReadNode(site, content); var node = context.Node;
            if (site.Input is not null || node.IntrinsicKind is not null || node.ChildSlots.Count != 0 || node.Structure is not null || node.OutputCaptures.Count != 0 ||
                node.ExecutionPlacement != ActivityExecutionPlacement.Inline || node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.ClrActivity ||
                node.Metadata.ContainsKey(RuntimeMetadataKeys.StructuralFaultHandling) || node.Metadata.ContainsKey(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild) ||
                node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) || node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion) ||
                node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) && executionType != "Action")
                return ValueTask.FromResult(Failed("delay_activity_shape_mismatch"));
            var contract = node.ActivityContract;
            if (contract is null) return ValueTask.FromResult(Failed("delay_contract_unsupported", ReleaseSemanticStatus.Unsupported));
            var type = new ValueTypeDescriptor("TimeSpan");
            if (contract.ActivityTypeKey != node.ActivityType || contract.ContractVersion != node.ActivityTypeVersion ||
                contract.DescriptorKind != typeof(ClrActivityDescriptor).FullName || contract.Activation.DescriptorKind != contract.DescriptorKind ||
                contract.Activation.Capability != Alias || !JsonElement.DeepEquals(contract.DescriptorPayload, node.DescriptorPayload) ||
                contract.SideEffectProfile != SideEffectProfile.External || contract.SupportsBackgroundExecution ||
                contract.Result.Type != new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))) || contract.Result.Projections.Count != 0 ||
                !contract.Outcomes.ToHashSet(StringComparer.Ordinal).SetEquals(["Done"]) || contract.Inputs.Count != 1 ||
                !contract.Inputs.TryGetValue(nameof(Delay.Duration), out var input) || input.Type != type || input.IsCredential ||
                input.ConnectionType is not null || input.IsStructureInput || input.IsNullable || node.InputBindings.Keys.Any(key => key != nameof(Delay.Duration)))
                return ValueTask.FromResult(Failed("delay_contract_mismatch"));
            if (input.HasDefault && !Duration(input.DefaultValue!.Value)) return ValueTask.FromResult(Failed("delay_default_mismatch"));
            if (node.InputBindings.TryGetValue(nameof(Delay.Duration), out var binding))
            {
                if (binding.TargetType != type) return ValueTask.FromResult(Failed("delay_binding_type_mismatch"));
                if (binding.Source == RuntimeInputBindingSource.SecretRead || binding.Literal?.WithheldValue?.Kind == WithheldValueKind.SecretReference)
                    return ValueTask.FromResult(Failed("delay_secret_binding_refused"));
                if (binding.ConversionPlan is { Operation: not ValueConversionOperation.Identity })
                    return ValueTask.FromResult(Failed("delay_conversion_unsupported", ReleaseSemanticStatus.Unsupported));
                if (binding.ConversionPlan is { } plan && (!ValueConversionCompatibility.SameType(plan.SourceType, type) || !ValueConversionCompatibility.SameType(plan.TargetType, type)))
                    return ValueTask.FromResult(Failed("delay_conversion_mismatch"));
                if (binding.LiteralValue is { } literal && !Duration(literal)) return ValueTask.FromResult(Failed("delay_literal_mismatch"));
            }
            var matches = Resumes(context, content).ByNode.GetValueOrDefault(node.ExecutableNodeId) ?? [];
            if (matches.Length != 1 || matches[0].Key != matches[0].Value.ResumeTargetId || matches[0].Value.HandlerKey != "ResumeAsync" ||
                !(matches[0].Value.LocalResumeTargetId == LocalResumeId || matches[0].Value.LocalResumeTargetId is null && matches[0].Key == LocalResumeId))
                return ValueTask.FromResult(Failed("delay_resume_target_mismatch"));
            if (matches[0].Value.Metadata.Count != 0) return ValueTask.FromResult(Failed("delay_resume_metadata_unsupported", ReleaseSemanticStatus.Unsupported));
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified, prerequisites:
                [Required("activity-module", "ActivitiesScheduling"), Required("timer-provider", Delay.TimerTriggerProviderId)]));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ReleaseRuntimeArtifactException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_delay_captured_content")); }

        ReleaseOccurrenceFacts Failed(string code, ReleaseSemanticStatus status = ReleaseSemanticStatus.Invalid) => new(status, diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
        ReleaseSemanticPrerequisite Required(string kind, string identity) => new(
            JsonSerializer.Serialize(new object?[] { site.Member.Kind, site.Member.Identity, site.Origin, site.Node, kind, identity }), site.Member, kind, identity, "opaque", "required");
    }

    private static bool Duration(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        TimeSpan.TryParseExact(value.GetString(), "c", CultureInfo.InvariantCulture, out _);

    private ResumeIndex Resumes(ReleaseRuntimeNodeContext context, IReleaseContentReader content) => _resumes.GetValue(content, _ => new())
        .GetOrAdd(context.Member.Key, _ => new(() => new(context.Workflow?.ResumeTargets ?? context.Template!.ResumeTargets), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    private sealed class ResumeIndex(IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> targets)
    {
        public Dictionary<string, KeyValuePair<string, WorkflowExecutableResumeTarget>[]> ByNode { get; } = targets
            .GroupBy(pair => pair.Value.ExecutableNodeId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }
}
