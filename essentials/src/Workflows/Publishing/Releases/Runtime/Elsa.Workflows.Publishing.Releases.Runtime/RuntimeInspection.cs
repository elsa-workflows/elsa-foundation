using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Runtime-owned inventory only; opaque module facts are mandatory independently dispatched sites.</summary>
internal sealed class RuntimeInspection(ReleaseMemberKey member, RuntimeCapturedContentCache cache, IReleaseContentReader content, CancellationToken token)
{
    private readonly HashSet<ReleaseSemanticEdge> _edges = [];
    private readonly HashSet<ReleaseSemanticPrerequisite> _prerequisites = [];
    private readonly Dictionary<string, ReleaseResourceRequirement> _resources = new(StringComparer.Ordinal);
    private readonly List<ReleaseSemanticSite> _sites = [];
    private readonly List<ReleaseSemanticDiagnostic> _diagnostics = [];
    private ReleaseSemanticStatus _status = ReleaseSemanticStatus.Verified;
    private const int MaximumSites = 100_000;

    public void Scope(ReleaseRuntimeContentScope scope)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.Authority)) throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_source_scope");
    }
    public void Invalid(string code) { _status = ReleaseSemanticStatus.Invalid; _diagnostics.Add(new(code, member)); }
    private void Unsupported(string code)
    {
        if (_status != ReleaseSemanticStatus.Invalid) _status = ReleaseSemanticStatus.Unsupported;
        _diagnostics.Add(new(code, member));
    }
    public ReleaseSemanticFacts Facts(ReleaseMemberKey identity, ReleaseOwnerVersion? version, IReadOnlyList<ReleaseRootCapability>? roots = null) =>
        new(_status, identity, version, true, _edges.OrderBy(x => x.Occurrence, StringComparer.Ordinal).ThenBy(x => x.To.Identity, StringComparer.Ordinal).ToArray(),
            _resources.Values.ToArray(), _prerequisites.ToArray(), _sites, _diagnostics, roots);
    public void Edge(ReleaseMemberKey target, ReleaseEdgeRelation relation, string occurrence) => _edges.Add(new(member, target, relation, occurrence));

    public (ReleaseMemberKey Key, WorkflowExecutableIdentity Pin) RuntimePin(string artifactId, ReleaseRuntimeContentScope scope)
    {
        return cache.RuntimePin(content, artifactId, scope);
    }

    public void Workflow(WorkflowExecutable workflow, ReleaseRuntimeContentScope scope)
    {
        var claimedNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in workflow.Dependencies)
        {
            token.ThrowIfCancellationRequested();
            var target = RuntimePin(dependency.ArtifactId, scope);
            if (target.Pin.ArtifactHash != dependency.ArtifactHash) Invalid("native_dependency_hash_mismatch");
            foreach (var nodeId in dependency.DispatchNodeIds)
            {
                if (!workflow.NodesById.ContainsKey(nodeId) || !claimedNodes.Add(nodeId)) Invalid("native_dependency_node_conflict");
                Edge(target.Key, ReleaseEdgeRelation.Dependency, nodeId);
            }
        }
        Requirements(workflow.RuntimeRequirements, workflow.StorageDriverRequirements);
        Require("incident-strategy", workflow.IncidentStrategy.Alias, "exact", workflow.IncidentStrategy.Version);
        if (workflow.PersistenceMode is { } mode) Require("persistence-mode", mode.ToString(), "opaque", "required");
        foreach (var input in workflow.InputContract!.Inputs) Type(input.Type.Alias);
        foreach (var variable in workflow.WorkflowVariables)
        {
            Type(variable.Type.Alias); Policy(variable.Policy, [], "variable:" + variable.VariableKey);
            if (variable.InitialBinding is { } binding) Binding(workflow.RootActivity, [], binding, "variable:" + variable.VariableKey);
        }
        Nodes(workflow.Nodes, workflow.RootActivity, scope, workflow.CompatibilityMetadata);
    }

    public void Template(ExecutableActivityTemplate template, ReleaseRuntimeContentScope scope)
    {
        if (template.DirectDependencies.Select(x => x.OccurrenceId).Distinct(StringComparer.Ordinal).Count() != template.DirectDependencies.Count)
            Invalid("template_occurrence_conflict");
        var occurrences = template.DirectDependencies.ToDictionary(x => x.OccurrenceId, StringComparer.Ordinal);
        foreach (var dependency in template.DirectDependencies)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(dependency.OccurrenceId) || dependency.ChildIndex < 0 || string.IsNullOrWhiteSpace(dependency.ChildSlotName) ||
                dependency.ParentOccurrenceId is not null && !occurrences.ContainsKey(dependency.ParentOccurrenceId)) Invalid("invalid_template_occurrence");
            var seen = new HashSet<string>(StringComparer.Ordinal); var current = dependency;
            while (current.ParentOccurrenceId is { } parent && occurrences.TryGetValue(parent, out var next))
            { if (!seen.Add(parent)) { Invalid("template_occurrence_cycle"); break; } current = next; }
            var child = TemplateByPin(dependency.TemplateId, dependency.TemplateHash, scope);
            var version = ActivityByVersion(dependency.DefinitionVersionId, scope);
            if (version.DefinitionId != dependency.DefinitionId || version.Version != dependency.Version || version.TemplateId != child.TemplateId || version.TemplateHash != child.TemplateHash)
                Invalid("template_version_association_mismatch");
            Edge(new(ReleaseRuntimeCodecs.TemplateKind, child.TemplateId), ReleaseEdgeRelation.Dependency, dependency.OccurrenceId);
            Edge(new(ReleaseRuntimeCodecs.ActivityVersionKind, version.VersionId), ReleaseEdgeRelation.Association, dependency.OccurrenceId);
            if (dependency.OccurrenceOverlay is not null)
                Site(template.Root, Origin(dependency.NodeOrigin), dependency.OccurrenceId, ReleaseRuntimeSiteKinds.TemplateOverlay, template.Root.Descriptor.ConsumerKey);
        }
        var closure = new HashSet<(string Id, string Hash)>(); var active = new HashSet<string>(StringComparer.Ordinal);
        Visit(template, 0);
        var declared = template.ClosedTemplates.Select(x => (x.TemplateId, x.TemplateHash)).ToArray();
        if (declared.Distinct().Count() != declared.Length || !closure.SetEquals(declared)) Invalid("template_closed_dag_mismatch");
        Requirements(template.RuntimeRequirements, template.StorageDriverRequirements);
        Require("compiler-fingerprint", template.ProviderFingerprint, "opaque", "producer-provenance");
        Nodes(template.NodesById.Values, template.Root, scope, template.CompatibilityMetadata);
        return;

        void Visit(ExecutableActivityTemplate current, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 128 || closure.Count > 10_000) throw ReleaseRuntimeArtifactCodec.Refuse("template_closure_limit");
            if (!active.Add(current.TemplateId)) throw ReleaseRuntimeArtifactCodec.Refuse("template_dependency_cycle");
            foreach (var dependency in current.DirectDependencies)
            {
                var child = TemplateByPin(dependency.TemplateId, dependency.TemplateHash, scope);
                if (active.Contains(child.TemplateId)) throw ReleaseRuntimeArtifactCodec.Refuse("template_dependency_cycle");
                if (closure.Add((child.TemplateId, child.TemplateHash))) Visit(child, depth + 1);
            }
            active.Remove(current.TemplateId);
        }
    }

    public void ActivityVersion(ReleaseActivityVersion activity)
    {
        if (string.IsNullOrWhiteSpace(activity.DefinitionId) || !SemVer.TryParse(activity.Version, out _) || string.IsNullOrWhiteSpace(activity.ActivityTypeKey) ||
            activity.ResolutionKind is not ("AuthorableActivity" or "ReusableTemplateBoundary") || string.IsNullOrWhiteSpace(activity.SourceReferenceId) ||
            string.IsNullOrWhiteSpace(activity.SourceKind) || string.IsNullOrWhiteSpace(activity.SourceId) || activity.Contract.ValueKind != JsonValueKind.Object || activity.ProviderPayload.ValueKind != JsonValueKind.Object)
            Invalid("invalid_activity_version_projection");
        var template = TemplateByPin(activity.TemplateId, activity.TemplateHash, activity.Scope);
        if (template.ProviderFingerprint != activity.CompilerFingerprint) Invalid("activity_compiler_fingerprint_mismatch");
        Edge(new(ReleaseRuntimeCodecs.TemplateKind, template.TemplateId), ReleaseEdgeRelation.Association, "");
        _sites.Add(new(member, [], "$catalog", null, new(ReleaseRuntimeSiteKinds.PublicContract, "elsa.activity-contract", 1), JsonSerializer.SerializeToElement(new { versionId = activity.VersionId })));
        _sites.Add(new(member, [], "$catalog", null, new(ReleaseRuntimeSiteKinds.Provider, activity.ProviderKey, 1), JsonSerializer.SerializeToElement(new { versionId = activity.VersionId })));
    }

    private ExecutableActivityTemplate TemplateByPin(string id, string hash, ReleaseRuntimeContentScope scope)
    {
        var descriptor = cache.Member(content, new(ReleaseRuntimeCodecs.TemplateKind, id));
        if (descriptor.Codec != new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1)) throw ReleaseRuntimeArtifactCodec.Refuse("captured_template_codec_unsupported");
        var target = cache.Template(content, descriptor.Key);
        if (target.Template.TemplateId != id || target.Template.TemplateHash != hash || target.Scope != scope || descriptor.OwnerVersion is not null)
            throw ReleaseRuntimeArtifactCodec.Refuse("captured_template_identity_conflict");
        return target.Template;
    }
    private ReleaseActivityVersion ActivityByVersion(string id, ReleaseRuntimeContentScope scope)
    {
        var descriptor = cache.Member(content, new(ReleaseRuntimeCodecs.ActivityVersionKind, id));
        if (descriptor.Codec.Kind != ReleaseRuntimeCodecs.ActivityVersionKind || descriptor.Codec.Codec != ReleaseRuntimeCodecs.ActivityVersion ||
            descriptor.Codec.CodecVersion is not (1 or 2)) throw ReleaseRuntimeArtifactCodec.Refuse("captured_activity_codec_unsupported");
        var target = cache.ActivityVersion(content, descriptor.Key);
        if (target.VersionId != id || target.Scope != scope || descriptor.OwnerVersion != new ReleaseOwnerVersion("semver", target.Version, null))
            throw ReleaseRuntimeArtifactCodec.Refuse("captured_activity_identity_conflict");
        return target;
    }

    private void Nodes(IEnumerable<ExecutableNode> nodes, ExecutableNode root, ReleaseRuntimeContentScope scope, IReadOnlyDictionary<string, string> compatibility)
    {
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested(); var origin = NodeOrigin(node);
            if (node.Descriptor.ConsumerKey == WellKnownRuntimeActivityConsumers.ClrActivity)
            {
                var fields = new[] { "typeAlias", "definitionId", "definitionVersionId", "version", "templateHash", "sourceReferenceId" };
                if (node.Descriptor.SchemaVersion != "1" || node.Descriptor.Payload.ValueKind != JsonValueKind.Object ||
                    node.Descriptor.Payload.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)) ||
                    node.Descriptor.Payload.EnumerateObject().Count() != 1 && (node.Descriptor.Payload.EnumerateObject().Count() != fields.Length || !node.Metadata.ContainsKey("activity.templateHash")) ||
                    !node.Descriptor.Payload.TryGetProperty("typeAlias", out var alias) || alias.ValueKind != JsonValueKind.String)
                    Unsupported("clr_descriptor_unsupported");
                else { Type(alias.GetString()!); Site(node, origin, null, ReleaseRuntimeSiteKinds.ClrActivity, alias.GetString()!); }
            }
            else if (!Intrinsic(node)) Site(node, origin, null, ReleaseRuntimeSiteKinds.Descriptor, node.Descriptor.ConsumerKey);
            if (node.Structure is { } structure) Site(node, origin, null, ReleaseRuntimeSiteKinds.Structure, structure.Kind);
            foreach (var slot in node.ChildSlots)
                if (slot.OperatorSchedulingCapability is { } capability)
                { Require("operator-policy", capability.PolicyKey, "exact", capability.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)); Site(node, origin, slot.Name, ReleaseRuntimeSiteKinds.Operator, capability.PolicyKey); }
            ChildControl(node, origin);
            foreach (var (key, value) in node.Metadata)
            {
                if (key == "authoredNodeId") { if (value != node.AuthoredActivityId) Invalid("authored_node_alias_mismatch"); continue; }
                if (key == TriggerNodeMetadata.ExecutionTypeKey && value is "Action" or "Trigger") continue;
                if (key is RuntimeMetadataKeys.ChildControlConsumerKey or RuntimeMetadataKeys.ChildControlConsumerVersion) continue;
                Site(node, origin, null, ReleaseRuntimeSiteKinds.Metadata, key);
            }
            Placement(node, origin, scope);
            foreach (var binding in node.InputBindings.Values) Binding(node, origin, binding, binding.InputName);
            foreach (var output in node.OutputCaptures.Values)
            {
                if (output.Type.Kind == "alias" && output.Type.Id is { } alias) Type(alias);
                else Unsupported("runtime_value_type_unsupported");
                Require("storage-driver", output.StorageDriverKey, "opaque", "required");
                Conversion(node, origin, output.OutputName, output.ConversionPlan);
                if (output.Metadata.Count != 0) Unsupported("output_metadata_unsupported");
            }
            if (node.ActivityContract is { } contract)
            {
                Type(contract.Result.Type.Alias);
                foreach (var input in contract.Inputs.Values)
                {
                    Type(input.Type.Alias);
                    if (input.IsCredential && node.InputBindings.TryGetValue(input.Key, out var binding) && binding.Source != RuntimeInputBindingSource.SecretRead)
                        Invalid("credential_literal_refused");
                    if (input.IsCredential && input.HasDefault) Invalid("credential_default_refused");
                    if (!node.InputBindings.ContainsKey(input.Key) && input.HasDefault && input.ConnectionType is not null)
                        Connection(origin, input.Key, input.DefaultValue, input.ConnectionType);
                }
            }
        }
        if (compatibility.Count != 0) Site(root, NodeOrigin(root), null, ReleaseRuntimeSiteKinds.Compatibility, "artifact");
    }

    private void ChildControl(ExecutableNode node, IReadOnlyList<string> origin)
    {
        var hasKey = node.Metadata.TryGetValue(RuntimeMetadataKeys.ChildControlConsumerKey, out var key);
        var hasVersion = node.Metadata.TryGetValue(RuntimeMetadataKeys.ChildControlConsumerVersion, out var rawVersion);
        if (!hasKey && !hasVersion) return;
        if (!hasKey || !hasVersion || string.IsNullOrWhiteSpace(key) ||
            !int.TryParse(rawVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) || version <= 0 ||
            rawVersion != version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { Invalid("invalid_child_control_consumer"); return; }
        if (_sites.Count >= MaximumSites) throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_limit");
        _sites.Add(new(member, origin, node.ExecutableNodeId, null, new(ReleaseRuntimeSiteKinds.ChildControlConsumer, key, version),
            JsonSerializer.SerializeToElement(new { nodeId = node.ExecutableNodeId })));
    }

    private bool Intrinsic(ExecutableNode node)
    {
        if (node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.Intrinsic) return false;
        if (node.IntrinsicKind is null || node.Descriptor.SchemaVersion != "1" || node.ActivityContract is not null ||
            node.Descriptor.Payload.ValueKind != JsonValueKind.Object || node.Descriptor.Payload.EnumerateObject().Count() != 2 ||
            !node.Descriptor.Payload.TryGetProperty("kind", out var kind) || kind.GetString() != node.IntrinsicKind.ToString() ||
            !node.Descriptor.Payload.TryGetProperty("schemaVersion", out var version) || version.GetString() != "1.0.0") Invalid("invalid_intrinsic_descriptor");
        return true;
    }

    private void Placement(ExecutableNode node, IReadOnlyList<string> origin, ReleaseRuntimeContentScope scope)
    {
        if (!node.Metadata.TryGetValue("activity.templateHash", out var hash)) return;
        if (!node.Metadata.TryGetValue("activity.definitionVersionId", out var versionId) || !node.Metadata.TryGetValue("activity.definitionId", out var definitionId) ||
            !node.Metadata.TryGetValue("activity.version", out var version) || !node.Metadata.TryGetValue("activity.sourceReferenceId", out var sourceId))
        { Invalid("incomplete_placement_association"); return; }
        var activity = ActivityByVersion(versionId, scope);
        if (activity.DefinitionId != definitionId || activity.Version != version || activity.TemplateHash != hash || activity.SourceReferenceId != sourceId)
            Invalid("placement_catalog_association_mismatch");
        _ = TemplateByPin(activity.TemplateId, hash, scope);
        Edge(new(ReleaseRuntimeCodecs.TemplateKind, activity.TemplateId), ReleaseEdgeRelation.Dependency, node.ExecutableNodeId);
        Edge(new(ReleaseRuntimeCodecs.ActivityVersionKind, versionId), ReleaseEdgeRelation.Association, node.ExecutableNodeId);
        Site(node, origin, null, ReleaseRuntimeSiteKinds.Placement, "activity-template");
    }

    private void Binding(ExecutableNode node, IReadOnlyList<string> origin, RuntimeInputBinding binding, string input)
    {
        Type(binding.TargetType.Alias); Policy(binding.EffectivePolicy, origin, input);
        if (binding.Selection is { } selection)
        {
            Require("runtime-consumer", RuntimeReadSelection.RequirementKey, "exact", selection.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (selection.SelectedType is { } selectedType) Type(selectedType.Alias);
            if (selection.ProfileRevision is { } revision) Require("activity-value-profile", "activity-value-json", "exact", revision);
        }
        if (binding.ConversionRequest is not null) Unsupported("unlinked_conversion_unsupported");
        Conversion(node, origin, input, binding.ConversionPlan);
        if (binding.Source == RuntimeInputBindingSource.SecretRead)
        {
            Resource(origin.Concat(new[] { "input", input }).ToArray(), binding.Secret!.Name, "secret"); Require("secret-resolver", "runtime", "opaque", "required");
        }
        if (binding.Literal?.WithheldValue is { Kind: WithheldValueKind.SecretReference, Secret: { } secret } withheld)
        {
            Resource(origin.Concat(new[] { "input", input }).ToArray(), secret.Name, "secret"); Require("secret-resolver", "runtime", "opaque", "required");
            if (withheld.ConversionPlan?.Fingerprint != binding.ConversionPlan?.Fingerprint) Conversion(node, origin, input, withheld.ConversionPlan);
        }
        if (binding.Literal?.HoldsValueRequiringEncryption(binding.EffectivePolicy) == true) Invalid("encryption_required_literal_refused");
        if (binding.Source == RuntimeInputBindingSource.Expression)
        {
            Site(node, origin, input, ReleaseRuntimeSiteKinds.Expression, binding.Expression!.Language);
            Require("expression-profile", binding.Expression.CapabilityProfile, "opaque", "required");
            foreach (var plan in binding.Expression.ParameterConversionPlans)
                Conversion(node, origin.Concat(new[] { "expression-parameter", plan.Key }).ToArray(), input, plan.Value);
        }
        if (binding.TargetType.Alias == typeof(ConnectionReference).FullName || node.ActivityContract?.Inputs.GetValueOrDefault(input)?.ConnectionType is not null)
        {
            if (binding.Source != RuntimeInputBindingSource.Literal) Unsupported("dynamic_connection_reference_unsupported");
            else Connection(origin, input, binding.LiteralValue, node.ActivityContract?.Inputs.GetValueOrDefault(input)?.ConnectionType);
        }
        if (binding.Literal is { ExternalReference: not null } || binding.Literal?.WithheldValue is { Kind: WithheldValueKind.PolicyRequiresEncryption })
            Unsupported("external_or_withheld_value_unsupported");
        foreach (var metadata in binding.Metadata)
        {
            if (metadata.Key == "referenceKey" && metadata.Value == binding.InputName) continue;
            Site(node, origin, input, ReleaseRuntimeSiteKinds.InputMetadata, metadata.Key);
        }
    }

    private void Connection(IReadOnlyList<string> origin, string input, JsonElement? value, string? requiredType)
    {
        if (value is null || value.Value.ValueKind == JsonValueKind.Null) return;
        var json = value.Value;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("connection", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()) || json.EnumerateObject().Any(x => x.Name is not ("connection" or "type")))
        { Invalid("invalid_connection_reference"); return; }
        if (json.TryGetProperty("type", out var type) && type.ValueKind != JsonValueKind.Null &&
            (type.ValueKind != JsonValueKind.String || requiredType is not null && !string.Equals(requiredType, type.GetString(), StringComparison.OrdinalIgnoreCase)))
        { Invalid("connection_type_mismatch"); return; }
        Resource(origin.Concat(new[] { "input", input }).ToArray(), name.GetString()!, "connection");
    }

    private void Conversion(ExecutableNode node, IReadOnlyList<string> origin, string input, ValueConversionPlan? plan)
    {
        if (plan is null) return;
        Type(plan.SourceType.Alias); Type(plan.TargetType.Alias);
        if (plan.Profile is { } profile)
        { Require("conversion-profile", profile.Id, "exact", profile.Version); Site(node, origin, input, ReleaseRuntimeSiteKinds.Conversion, profile.Id); }
        else if (plan.Options is { } options && options.EnumerateObject().Any()) Unsupported("conversion_options_unsupported");
    }
    private void Policy(ValueProtectionPolicy policy, IReadOnlyList<string> origin, string input)
    {
        if (policy.RetentionPolicy is { } retention) Require("retention-policy", retention, "opaque", "required");
        if (policy.Metadata.Count != 0) Unsupported("value_policy_metadata_unsupported");
    }
    public void Requirements(IEnumerable<RuntimeRequirement> consumers, IEnumerable<RuntimeStorageDriverRequirement> drivers)
    {
        foreach (var requirement in consumers) Require("runtime-consumer", requirement.ConsumerKey, "exact", requirement.SchemaVersion);
        foreach (var driver in drivers) Require("storage-driver", driver.DriverKey, "opaque", "required");
    }
    private void Type(string alias) => Require("value-type", alias, "opaque", "required");
    private void Require(string kind, string identity, string scheme, string constraint)
    {
        var key = JsonSerializer.Serialize(new[] { member.Kind, member.Identity, kind, identity, scheme, constraint });
        _prerequisites.Add(new(key, member, kind, identity, scheme, constraint));
    }
    private void Resource(IReadOnlyList<string> origin, string reference, string kind)
    {
        var key = JsonSerializer.Serialize(new object[] { origin, reference, kind });
        _resources.TryAdd(key, new(member, origin, reference, kind, "1"));
    }
    public void ModelSite(RuntimeModelMaterial model) => _sites.Add(new(member, [], "$model", null,
        new(ReleaseRuntimeSiteKinds.ModelProtocol, model.Protocol.Key, model.Protocol.Version), JsonSerializer.SerializeToElement(new { protocolKey = model.Protocol.Key, protocolVersion = model.Protocol.Version })));
    private void Site(ExecutableNode node, IReadOnlyList<string> origin, string? input, string kind, string discriminator)
    {
        if (_sites.Count >= MaximumSites) throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_limit");
        _sites.Add(new(member, origin, node.ExecutableNodeId, input, new(kind, discriminator, 1), JsonSerializer.SerializeToElement(new { nodeId = node.ExecutableNodeId })));
    }
    private static IReadOnlyList<string> NodeOrigin(ExecutableNode node)
    {
        if (!node.Metadata.TryGetValue("activity.invocationOrigin", out var text)) return [node.ExecutableNodeId];
        ReleaseRuntimeArtifactCodec.CheckJson(System.Text.Encoding.UTF8.GetBytes(text), new());
        var segments = JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(text) ?? throw ReleaseRuntimeArtifactCodec.Refuse("invalid_placement_origin");
        var origin = new ActivityInvocationOrigin(segments);
        if (segments.Any(x => !Enum.IsDefined(x.Kind) || string.IsNullOrWhiteSpace(x.Id)) ||
            !node.Metadata.TryGetValue("activity.placementNamespace", out var stored) || stored != origin.ComputePlacementNamespace() ||
            !node.Metadata.TryGetValue("activity.templateNodeId", out var templateNode) || string.IsNullOrWhiteSpace(templateNode))
            throw ReleaseRuntimeArtifactCodec.Refuse("invalid_placement_origin");
        return Origin(origin);
    }
    private static IReadOnlyList<string> Origin(ActivityInvocationOrigin origin) => origin.Segments.Select(x => JsonSerializer.Serialize(new object[] { (byte)x.Kind, x.Id })).ToArray();
}
