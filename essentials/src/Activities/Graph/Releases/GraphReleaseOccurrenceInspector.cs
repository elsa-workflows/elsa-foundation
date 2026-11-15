using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Releases;

/// <summary>Closed single-entry wrapper profile. Structural authored trees and richer bindings require further owner qualification.</summary>
internal sealed class GraphReleaseOccurrenceInspector(IReleaseRuntimeSiteReader reader) : IReleaseOccurrenceInspector
{
    private const string ProviderKey = "elsa.activity-graph";
    private const string Fingerprint = "elsa.activity-graph/compiler/4.0.0";
    private static readonly string[] PlanFields = ["planSchemaVersion", "entryOccurrenceId", "occurrences", "variables", "outputMappings", "inputs", "outputs",
        "inputReferenceKeys", "outputReferenceKeys", "requiredInputReferenceKeys", "requiredOutputReferenceKeys", "scopeModel", "outcomeMappings", "completeOutcomes"];
    private static readonly string[] PlacedFields = ["definitionId", "definitionVersionId", "version", "templateHash", "sourceReferenceId", "entryNodeId"];
    private readonly ConditionalWeakTable<IReleaseContentReader, ConcurrentDictionary<ReleaseMemberKey, Lazy<HashSet<string>>>> _dependencies = new();
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.graph-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Descriptor, WellKnownRuntimeActivityConsumers.GraphActivity, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Provider, ProviderKey, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.TemplateOverlay, WellKnownRuntimeActivityConsumers.GraphActivity, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, "graph.templateHash", 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, RuntimeMetadataKeys.ActivityExecutionScope, 1)
    });
    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) throw Refuse("graph_site_unsupported");
            if (site.Site.SiteKind == ReleaseRuntimeSiteKinds.Provider)
            {
                if (site.Node != "$catalog" || site.Input is not null || site.Origin.Count != 0) throw Refuse("invalid_graph_provider_site");
                Fields(site.Payload, "versionId"); if (Text(site.Payload, "versionId") != site.Member.Identity) throw Refuse("invalid_graph_provider_site");
                var activity = reader.ReadActivityVersion(site.Member, content); var template = Template(activity, content);
                Provider(activity, template, content); return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified));
            }
            var context = reader.ReadNode(site, content);
            if (site.Site.SiteKind == ReleaseRuntimeSiteKinds.TemplateOverlay)
            {
                if (context.Template is null || context.Template.Root.ExecutableNodeId != site.Node || site.Input is null) throw Refuse("invalid_graph_overlay_site");
                var dependency = context.Template.DirectDependencies.Single(item => item.OccurrenceId == site.Input);
                ValidatePlan(context.Template, content);
                var child = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, dependency.TemplateId), content);
                var overlay = dependency.OccurrenceOverlay ?? throw Refuse("graph_overlay_missing");
                if (overlay.SchemaVersion != "1") throw Refuse("graph_overlay_schema_unsupported");
                if (overlay.Structure is not null || overlay.InputContracts is not null || overlay.OutputBindings is { } outputs && outputs.GetArrayLength() != 0)
                    throw Refuse("graph_overlay_surface_unsupported");
                Inputs(overlay.InputBindings, context.Template.Root.Descriptor.Payload.GetProperty("inputs"), child.Template.Root.ActivityContract);
                return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified));
            }
            if (site.Input is not null || context.Node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.GraphActivity || context.Node.Descriptor.SchemaVersion != "1" ||
                context.Node.IntrinsicKind is not null || context.Node.Structure is not null || context.Node.Metadata.ContainsKey(RuntimeMetadataKeys.StructuralFaultHandling) ||
                context.Node.Metadata.ContainsKey(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild) || context.Node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) ||
                context.Node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion)) throw Refuse("graph_activity_shape_mismatch");
            var claims = _dependencies.GetValue(content, _ => new()).GetOrAdd(context.Member.Key,
                _ => new(() => (context.Workflow?.Dependencies ?? []).SelectMany(dependency => dependency.DispatchNodeIds).ToHashSet(StringComparer.Ordinal), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            if (claims.Contains(context.Node.ExecutableNodeId)) throw Refuse("graph_dependency_inventory_mismatch");
            ExecutableActivityTemplate captured;
            if (context.Template is not null)
            {
                captured = context.Template;
                if (site.Node != captured.Root.ExecutableNodeId || context.Node.ChildSlots.Count != 0) throw Refuse("graph_template_root_mismatch");
            }
            else
            {
                if (!context.Node.Metadata.TryGetValue("activity.definitionVersionId", out var versionId)) throw Refuse("graph_placement_association_missing");
                var activity = reader.ReadActivityVersion(new(ReleaseRuntimeCodecs.ActivityVersionKind, versionId), content); captured = Template(activity, content);
                if (activity.Scope != context.Scope) throw Refuse("graph_source_scope_mismatch");
                Fields(context.Node.Descriptor.Payload, [.. PlanFields, .. PlacedFields]);
                foreach (var field in captured.Root.Descriptor.Payload.EnumerateObject())
                    if (!context.Node.Descriptor.Payload.TryGetProperty(field.Name, out var actual) || !JsonElement.DeepEquals(field.Value, actual)) throw Refuse("graph_placed_plan_mismatch");
                foreach (var field in new[] { "definitionId", "definitionVersionId", "version", "templateHash", "sourceReferenceId" })
                    if (Text(context.Node.Descriptor.Payload, field) != context.Node.Metadata["activity." + field]) throw Refuse("graph_placed_identity_mismatch");
                var dependency = captured.DirectDependencies.Single();
                if (context.Node.ChildSlots.Count != 1 || context.Node.ChildSlots.Single().Name != dependency.ChildSlotName || context.Node.ChildSlots.Single().Activities.Count != 1 ||
                    Text(context.Node.Descriptor.Payload, "entryNodeId") != context.Node.ChildSlots.Single().Activities.Single().ExecutableNodeId)
                    throw Refuse("graph_entry_node_mismatch");
                PlacedInputs(context.Node.ChildSlots.Single().Activities.Single(), dependency.OccurrenceOverlay!);
                if (context.Node.Metadata.GetValueOrDefault("graph.templateHash") != captured.TemplateHash || context.Node.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) != RuntimeMetadataKeys.ActivityExecutionScopeClosed)
                    throw Refuse("graph_scope_metadata_mismatch");
                var sourceContract = captured.Root.ActivityContract ?? throw Refuse("graph_runtime_contract_missing");
                var placedContract = context.Node.ActivityContract ?? throw Refuse("graph_runtime_contract_missing");
                var expected = new ActivityContract(context.Node.ActivityType, context.Node.ActivityTypeVersion, sourceContract.DescriptorKind, context.Node.Descriptor.Payload,
                    sourceContract.Inputs.Values, sourceContract.Result, sourceContract.Outcomes, sourceContract.Activation, sourceContract.SideEffectProfile, sourceContract.SupportsBackgroundExecution);
                if (expected.SchemaFingerprint != placedContract.SchemaFingerprint || sourceContract.Inputs.Any(input => !placedContract.Inputs.TryGetValue(input.Key, out var actual) || actual.Name != input.Value.Name))
                    throw Refuse("graph_placed_contract_mismatch");
            }
            ValidatePlan(captured, content);
            if (site.Site.SiteKind != ReleaseRuntimeSiteKinds.Descriptor) return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified));
            var key = JsonSerializer.Serialize(new object?[] { site.Member.Kind, site.Member.Identity, site.Origin, site.Node, "activity-module", "ActivitiesGraphRuntime" });
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified, prerequisites: [new(key, site.Member, "activity-module", "ActivitiesGraphRuntime", "opaque", "required")]));
        }
        catch (OperationCanceledException) { throw; }
        catch (ProfileException exception) { return ValueTask.FromResult(Failed(exception.Code)); }
        catch (ReleaseRuntimeArtifactException exception) { return ValueTask.FromResult(Failed(exception.Code.Contains("unsupported", StringComparison.Ordinal) ? "graph_captured_kind_unsupported" : "invalid_graph_captured_content")); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_graph_captured_content")); }
        ReleaseOccurrenceFacts Failed(string code) => new(code.Contains("unsupported", StringComparison.Ordinal) ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid,
            diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }
    private ExecutableActivityTemplate Template(ReleaseActivityVersion activity, IReleaseContentReader content)
    {
        var template = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, activity.TemplateId), content);
        if (activity.Scope != template.Scope || activity.TemplateHash != template.Template.TemplateHash || activity.CompilerFingerprint != template.Template.ProviderFingerprint)
            throw Refuse("graph_catalog_template_mismatch");
        return template.Template;
    }
    private void Provider(ReleaseActivityVersion activity, ExecutableActivityTemplate template, IReleaseContentReader content)
    {
        if (activity.ProviderKey != ProviderKey || activity.ProviderSchemaVersion is not ("1" or "2") || activity.CompilerFingerprint != Fingerprint || activity.ResolutionKind != "ReusableTemplateBoundary")
            throw Refuse("graph_provider_profile_unsupported");
        Fields(activity.ProviderPayload, "rootActivity", "variables", "outputMappings", "outcomeMappings");
        Empty(activity.ProviderPayload, "variables"); Empty(activity.ProviderPayload, "outputMappings");
        var dependency = ValidatePlan(template, content); var node = activity.ProviderPayload.GetProperty("rootActivity");
        Fields(node, "nodeId", "activityVersionId", "inputs", "outputs", "structure", "intrinsic", "executionPlacement");
        if (node.TryGetProperty("structure", out var structure) && structure.ValueKind != JsonValueKind.Null || node.TryGetProperty("intrinsic", out var intrinsic) && intrinsic.ValueKind != JsonValueKind.Null)
            throw Refuse("graph_authored_structure_unsupported");
        if (node.TryGetProperty("executionPlacement", out var placement) && !(placement.ValueKind == JsonValueKind.Number && placement.GetInt32() == 0 || placement.ValueKind == JsonValueKind.String && placement.GetString() == "Inline"))
            throw Refuse("graph_execution_placement_unsupported");
        if (Text(node, "nodeId") != dependency.OccurrenceId || Text(node, "activityVersionId") != dependency.DefinitionVersionId) throw Refuse("graph_provider_dependency_mismatch");
        Empty(node, "outputs");
        var child = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, dependency.TemplateId), content);
        Inputs(node.GetProperty("inputs"), template.Root.Descriptor.Payload.GetProperty("inputs"), child.Template.Root.ActivityContract);
        if (dependency.OccurrenceOverlay is null || !JsonElement.DeepEquals(node.GetProperty("inputs"), dependency.OccurrenceOverlay.InputBindings)) throw Refuse("graph_provider_overlay_mismatch");
        if (activity.ProviderSchemaVersion == "1")
        {
            var emitted = activity.Contract.GetProperty("outcomes").EnumerateArray().Where(value => value.GetProperty("isEmitted").GetBoolean()).ToArray();
            if (emitted.Length != 1 || Text(emitted[0], "referenceKey") != "done") throw Refuse("graph_schema_one_outcome_mismatch");
        }
        if (activity.ProviderSchemaVersion == "1" && activity.ProviderPayload.TryGetProperty("outcomeMappings", out var legacy) && legacy.GetArrayLength() != 0) throw Refuse("graph_outcome_schema_mismatch");
        if (activity.ProviderSchemaVersion == "2") OutcomeMappings(activity.ProviderPayload.GetProperty("outcomeMappings"), template, content, authored: true, activity.Contract);
    }
    private ExecutableActivityTemplateDependency ValidatePlan(ExecutableActivityTemplate template, IReleaseContentReader content)
    {
        if (template.ProviderFingerprint != Fingerprint || template.Root.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.GraphActivity || template.Root.Descriptor.SchemaVersion != "1")
            throw Refuse("graph_template_profile_unsupported");
        var plan = template.Root.Descriptor.Payload; Fields(plan, PlanFields);
        if (Text(plan, "planSchemaVersion") != "1" || Text(plan, "scopeModel") != RuntimeMetadataKeys.ActivityExecutionScopeClosed) throw Refuse("graph_plan_scope_unsupported");
        var runtime = template.Root.ActivityContract ?? throw Refuse("graph_runtime_contract_missing");
        // The producer stamps the raw node with the Graph consumer, while the contract keeps the catalog activity type.
        // The public-contract owner checks that separate catalog identity; placement stamps its own full contract below.
        if (template.Root.ActivityType != WellKnownRuntimeActivityConsumers.GraphActivity || template.Root.ActivityTypeVersion != "1" ||
            runtime.ContractVersion != "1" || runtime.DescriptorKind != WellKnownRuntimeActivityConsumers.GraphActivity || runtime.Activation.DescriptorKind != runtime.DescriptorKind ||
            runtime.Activation.Capability != runtime.DescriptorKind || !JsonElement.DeepEquals(runtime.DescriptorPayload, plan) || runtime.SideEffectProfile != SideEffectProfile.External || runtime.SupportsBackgroundExecution)
            throw Refuse("graph_runtime_contract_mismatch");
        if (runtime.Result.Type != new ValueTypeDescriptor("Object") || !runtime.Result.IsRequired || runtime.Result.EffectiveSourceRepresentation != ValueRepresentation.StructuredValue ||
            runtime.Result.Projections.Count != 0 || template.Root.ChildSlots.Count != 0 || template.Root.ExecutionPlacement != ActivityExecutionPlacement.Inline ||
            template.Root.Structure is not null || template.Root.IntrinsicKind is not null || template.Root.OutputCaptures.Count != 0)
            throw Refuse("graph_template_surface_unsupported");
        var graphInputs = plan.GetProperty("inputs");
        if (graphInputs.ValueKind != JsonValueKind.Array || graphInputs.GetArrayLength() != runtime.Inputs.Count) throw Refuse("graph_public_input_inventory_mismatch");
        var inputKeys = new HashSet<string>(StringComparer.Ordinal); var storage = template.StorageDriverRequirements.Select(requirement => requirement.DriverKey).ToHashSet(StringComparer.Ordinal);
        foreach (var input in graphInputs.EnumerateArray())
        {
            Fields(input, "referenceKey", "name", "type", "storageDriverKey", "durability", "default"); var key = Text(input, "referenceKey");
            if (!inputKeys.Add(key) || !runtime.Inputs.TryGetValue(key, out var declared) || declared.Name != Text(input, "name") || !storage.Contains(Text(input, "storageDriverKey"))) throw Refuse("graph_public_input_inventory_mismatch");
            var type = input.GetProperty("type"); Fields(type, "alias", "collectionKind");
            if (Text(type, "alias") != declared.Type.Alias || Text(type, "collectionKind") != declared.Type.CollectionKind.ToString()) throw Refuse("graph_public_input_type_mismatch");
            if (input.TryGetProperty("durability", out var durability) && !(durability.ValueKind == JsonValueKind.Number && durability.GetInt32() == 0 || durability.ValueKind == JsonValueKind.String && durability.GetString() == "Required")) throw Refuse("graph_durability_unsupported");
            var hasDefault = input.TryGetProperty("default", out var value) && value.ValueKind != JsonValueKind.Null;
            if (hasDefault)
            {
                Fields(value, "syntax", "value");
                if (!string.Equals(Text(value, "syntax"), "Literal", StringComparison.OrdinalIgnoreCase)) throw Refuse("graph_default_syntax_unsupported");
                if (!declared.HasDefault || !declared.DefaultValue.HasValue || !JsonElement.DeepEquals(declared.DefaultValue.Value, value.GetProperty("value"))) throw Refuse("graph_default_inventory_mismatch");
            }
            else if (declared.HasDefault) throw Refuse("graph_default_inventory_mismatch");
        }
        Set(plan.GetProperty("inputReferenceKeys"), inputKeys);
        Set(plan.GetProperty("requiredInputReferenceKeys"), runtime.Inputs.Values.Where(input => input.IsRequired).Select(input => input.Key));
        Empty(plan, "variables"); Empty(plan, "outputMappings"); Empty(plan, "outputs"); Empty(plan, "outputReferenceKeys"); Empty(plan, "requiredOutputReferenceKeys");
        if (plan.TryGetProperty("completeOutcomes", out var complete) && complete.GetArrayLength() != 0) throw Refuse("graph_complete_profile_unsupported");
        if (template.DirectDependencies.Count != 1) throw Refuse("graph_structural_tree_unsupported");
        var dependency = template.DirectDependencies.Single();
        if (dependency.ParentOccurrenceId is not null || dependency.ChildIndex != 0 || dependency.ChildSlotName != "activity-graph" || Text(plan, "entryOccurrenceId") != dependency.OccurrenceId)
            throw Refuse("graph_entry_inventory_mismatch");
        var occurrences = plan.GetProperty("occurrences");
        if (occurrences.ValueKind != JsonValueKind.Array || occurrences.GetArrayLength() != 1) throw Refuse("graph_occurrence_inventory_mismatch");
        var occurrence = occurrences[0];
        Fields(occurrence, "occurrenceId", "definitionId", "definitionVersionId", "version", "templateId", "templateHash", "nodeOrigin", "parentOccurrenceId", "childSlotName", "childIndex", "authoredDepth", "inputBindings", "outputBindings");
        if (Text(occurrence, "occurrenceId") != dependency.OccurrenceId || Text(occurrence, "definitionId") != dependency.DefinitionId || Text(occurrence, "definitionVersionId") != dependency.DefinitionVersionId ||
            Text(occurrence, "version") != dependency.Version || Text(occurrence, "templateId") != dependency.TemplateId || Text(occurrence, "templateHash") != dependency.TemplateHash ||
            occurrence.GetProperty("childIndex").GetInt32() != 0 || Text(occurrence, "childSlotName") != dependency.ChildSlotName || occurrence.GetProperty("authoredDepth").GetInt32() != 1 ||
            occurrence.TryGetProperty("parentOccurrenceId", out var parent) && parent.ValueKind != JsonValueKind.Null) throw Refuse("graph_occurrence_inventory_mismatch");
        var origins = occurrence.GetProperty("nodeOrigin");
        if (origins.ValueKind != JsonValueKind.Array || origins.GetArrayLength() != dependency.NodeOrigin.Segments.Count) throw Refuse("graph_occurrence_origin_mismatch");
        for (var index = 0; index < origins.GetArrayLength(); index++)
        {
            Fields(origins[index], "kind", "id");
            if (Text(origins[index], "kind") != dependency.NodeOrigin.Segments[index].Kind.ToString() || Text(origins[index], "id") != dependency.NodeOrigin.Segments[index].Id) throw Refuse("graph_occurrence_origin_mismatch");
        }
        var overlay = dependency.OccurrenceOverlay ?? throw Refuse("graph_overlay_missing");
        if (overlay.SchemaVersion != "1") throw Refuse("graph_overlay_schema_unsupported");
        if (overlay.Structure is not null || overlay.InputContracts is not null || overlay.OutputBindings is { } output && output.GetArrayLength() != 0) throw Refuse("graph_overlay_surface_unsupported");
        if (!JsonElement.DeepEquals(occurrence.GetProperty("inputBindings"), overlay.InputBindings)) throw Refuse("graph_overlay_inventory_mismatch");
        if (occurrence.TryGetProperty("outputBindings", out var bindings) && bindings.ValueKind != JsonValueKind.Null && bindings.GetArrayLength() != 0) throw Refuse("graph_output_binding_unsupported");
        var child = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, dependency.TemplateId), content);
        Inputs(overlay.InputBindings, plan.GetProperty("inputs"), child.Template.Root.ActivityContract);
        if (plan.TryGetProperty("outcomeMappings", out var mappings)) OutcomeMappings(mappings, template, content, authored: false);
        return dependency;
    }
    private void OutcomeMappings(JsonElement mappings, ExecutableActivityTemplate template, IReleaseContentReader content, bool authored, JsonElement? publicContract = null)
    {
        if (mappings.ValueKind != JsonValueKind.Array || mappings.GetArrayLength() > 1000) throw Refuse("graph_outcome_mapping_limit");
        var dependency = template.DirectDependencies.Single(); var child = reader.ReadActivityVersion(new(ReleaseRuntimeCodecs.ActivityVersionKind, dependency.DefinitionVersionId), content);
        var sourceOutcomes = child.Contract.GetProperty("outcomes").EnumerateArray().Where(item => item.GetProperty("isEmitted").GetBoolean()).ToDictionary(item => Text(item, "referenceKey"), item => Text(item, "name"), StringComparer.Ordinal);
        var targetOutcomes = template.Root.ActivityContract?.Outcomes.ToHashSet(StringComparer.Ordinal) ?? throw Refuse("graph_runtime_contract_missing");
        var targetReferences = publicContract is { } declared
            ? declared.GetProperty("outcomes").EnumerateArray().Where(item => item.GetProperty("isEmitted").GetBoolean())
                .ToDictionary(item => Text(item, "referenceKey"), item => Text(item, "name"), StringComparer.Ordinal)
            : null;
        var actual = template.Root.Descriptor.Payload.TryGetProperty("outcomeMappings", out var planMappings) ? planMappings.EnumerateArray().ToArray() : [];
        if (authored && actual.Length != mappings.GetArrayLength()) throw Refuse("graph_provider_outcome_mapping_mismatch");
        var seen = new HashSet<string>(StringComparer.Ordinal); var covered = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        foreach (var mapping in mappings.EnumerateArray())
        {
            if (authored)
            {
                Fields(mapping, "sourceOutcomeReferenceKey", "boundaryOutcomeReferenceKey");
                if (!sourceOutcomes.TryGetValue(Text(mapping, "sourceOutcomeReferenceKey"), out var source) || targetReferences is null ||
                    !targetReferences.TryGetValue(Text(mapping, "boundaryOutcomeReferenceKey"), out var target) || !seen.Add(source) ||
                    Text(actual[position], "sourceOutcomeName") != source || Text(actual[position], "boundaryOutcomeName") != target) throw Refuse("graph_provider_outcome_mapping_mismatch");
            }
            else
            {
                Fields(mapping, "sourceOutcomeName", "boundaryOutcomeName");
                if (!sourceOutcomes.Values.Contains(Text(mapping, "sourceOutcomeName"), StringComparer.Ordinal) || !targetOutcomes.Contains(Text(mapping, "boundaryOutcomeName")) ||
                    !seen.Add(Text(mapping, "sourceOutcomeName"))) throw Refuse("graph_outcome_mapping_mismatch");
            }
            covered.Add(Text(authored ? actual[position] : mapping, "boundaryOutcomeName"));
            position++;
        }
        // Complete nodes are outside this profile, so every emitted outcome needs a mapping.
        if (targetOutcomes.Count == 0 || !covered.SetEquals(targetOutcomes)) throw Refuse("graph_outcome_coverage_mismatch");
    }
    private static void Inputs(JsonElement bindings, JsonElement graphInputs, ActivityContract? child)
    {
        if (child is null || bindings.ValueKind != JsonValueKind.Array || bindings.GetArrayLength() > 10_000) throw Refuse("graph_input_contract_unsupported");
        var publicInputs = graphInputs.EnumerateArray().ToDictionary(item => Text(item, "referenceKey"), StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings.EnumerateArray())
        {
            Fields(binding, "referenceKey", "value", "autoEvaluate", "evaluatorType", "storageDriverType", "isSensitive", "conversion");
            var key = Text(binding, "referenceKey");
            if (!seen.Add(key) || !child.Inputs.TryGetValue(key, out var input)) throw Refuse("graph_input_reference_mismatch");
            if (!TypeAliasConvention.ReservedBareAliases.Contains(input.Type.Alias) || input.Type.Schema is not null || input.IsCredential || input.ConnectionType is not null || input.Policy.RequiresEncryption) throw Refuse("graph_input_type_unsupported");
            foreach (var field in new[] { "evaluatorType", "storageDriverType", "conversion" })
                if (binding.TryGetProperty(field, out var extra) && extra.ValueKind != JsonValueKind.Null) throw Refuse("graph_binding_option_unsupported");
            if (binding.TryGetProperty("isSensitive", out var sensitive) && sensitive.ValueKind != JsonValueKind.Null && sensitive.ValueKind != JsonValueKind.False)
                throw Refuse("graph_sensitive_binding_unsupported");
            if (binding.TryGetProperty("autoEvaluate", out var automatic) && automatic.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)) throw Refuse("invalid_graph_auto_evaluate");
            var value = binding.GetProperty("value"); Fields(value, "value", "expressionType");
            var syntax = value.TryGetProperty("expressionType", out var expression) && expression.ValueKind != JsonValueKind.Null ? expression.GetString() : "Literal";
            if (string.Equals(syntax, "Input", StringComparison.OrdinalIgnoreCase))
            {
                var reference = value.GetProperty("value"); Fields(reference, "referenceKey");
                if (!publicInputs.TryGetValue(Text(reference, "referenceKey"), out var declared) || Text(declared.GetProperty("type"), "alias") != input.Type.Alias ||
                    declared.GetProperty("type").GetProperty("collectionKind").GetString() != input.Type.CollectionKind.ToString()) throw Refuse("graph_input_type_mismatch");
            }
            else if (!string.Equals(syntax, "Literal", StringComparison.OrdinalIgnoreCase)) throw Refuse("graph_binding_syntax_unsupported");
            else if (value.GetProperty("value").ValueKind == JsonValueKind.Null && !input.IsNullable) throw Refuse("graph_null_input_mismatch");
            else if (input.Type.Alias != "String" || input.Type.CollectionKind != CollectionKind.Single || value.GetProperty("value").ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw Refuse("graph_literal_type_unsupported");
        }
        if (child.Inputs.Values.Any(input => input.IsRequired && !input.HasDefault && !seen.Contains(input.Key))) throw Refuse("graph_required_input_missing");
    }
    private static void PlacedInputs(ExecutableNode node, ExecutableActivityTemplateOccurrenceOverlay overlay)
    {
        var contract = node.ActivityContract ?? throw Refuse("graph_runtime_contract_missing");
        var authored = overlay.InputBindings.EnumerateArray().ToDictionary(value => Text(value, "referenceKey"), StringComparer.Ordinal);
        // Implicit optional/default lowering needs its own profile; never infer it from the placed body.
        if (authored.Count != contract.Inputs.Count) throw Refuse("graph_implicit_input_profile_unsupported");
        if (node.InputBindings.Count != authored.Count) throw Refuse("graph_placed_input_inventory_mismatch");
        foreach (var (key, source) in authored)
        {
            if (!contract.Inputs.TryGetValue(key, out var input) || !node.InputBindings.TryGetValue(key, out var binding) || binding.InputKey != key || binding.TargetType != input.Type)
                throw Refuse("graph_placed_input_inventory_mismatch");
            var expectedPolicy = ValuePolicyCombiner.ToProtectionPolicy(input.Policy);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expectedPolicy), JsonSerializer.SerializeToElement(binding.EffectivePolicy)) ||
                binding.ConversionRequest is not null || binding.Metadata.Count != 1 || binding.Metadata.GetValueOrDefault("referenceKey") != key)
                throw Refuse("graph_placed_input_policy_mismatch");
            var value = source.GetProperty("value"); var syntax = value.TryGetProperty("expressionType", out var expression) && expression.ValueKind != JsonValueKind.Null ? expression.GetString() : "Literal";
            if (string.Equals(syntax, "Input", StringComparison.OrdinalIgnoreCase))
            {
                if (binding.Source != RuntimeInputBindingSource.WorkflowRequest || binding.WorkflowRequest is not { } request || request.MemberKey != Text(value.GetProperty("value"), "referenceKey") ||
                    request.Path is not null || binding.ConversionPlan is not null) throw Refuse("graph_placed_input_value_mismatch");
            }
            else
            {
                var literal = binding.Literal; var expected = value.GetProperty("value");
                if (!string.Equals(syntax, "Literal", StringComparison.OrdinalIgnoreCase) || binding.Source != RuntimeInputBindingSource.Literal || literal is null ||
                    literal.Type != input.Type || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expectedPolicy), JsonSerializer.SerializeToElement(literal.Policy)) ||
                    (expected.ValueKind == JsonValueKind.Null ? literal.Presence != ValuePresence.ExplicitNull : literal.Presence != ValuePresence.Present || !literal.InlineValue.HasValue || !JsonElement.DeepEquals(expected, literal.InlineValue.Value)) ||
                    binding.ConversionPlan?.Fingerprint != ValueConversionPlan.Identity(input.Type, ValueRepresentationDefaults.Infer(input.Type)).Fingerprint)
                    throw Refuse("graph_placed_input_value_mismatch");
            }
        }
    }
    private static void Set(JsonElement values, IEnumerable<string> expected)
    {
        if (values.ValueKind != JsonValueKind.Array) throw Refuse("graph_reference_inventory_mismatch");
        var keys = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Refuse("graph_reference_inventory_mismatch")).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || !keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) throw Refuse("graph_reference_inventory_mismatch");
    }
    private static void Empty(JsonElement value, string property)
    { var array = value.GetProperty(property); if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 0) throw Refuse("graph_surface_unsupported"); }
    private static string Text(JsonElement value, string property) => value.GetProperty(property) is { ValueKind: JsonValueKind.String } text && !string.IsNullOrWhiteSpace(text.GetString()) ? text.GetString()! : throw Refuse("invalid_graph_text");
    private static void Fields(JsonElement value, params string[] fields)
    { if (value.ValueKind != JsonValueKind.Object) throw Refuse("invalid_graph_object"); foreach (var property in value.EnumerateObject()) if (!fields.Contains(property.Name, StringComparer.Ordinal)) throw Refuse("graph_field_unsupported"); }
    private static ProfileException Refuse(string code) => new(code);
    private sealed class ProfileException(string code) : Exception(code) { public string Code { get; } = code; }
}
