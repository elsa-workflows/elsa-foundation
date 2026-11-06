using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Releases.Core;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Releases.Workflows;

/// <summary>The real native leaf owner's retained contract, independent of publisher or runtime store lookups.</summary>
internal sealed class RunEtlReleaseInspector(IReleaseRuntimeSiteReader sites, IEtlReleaseArtifactCodec codec,
    EtlCapturedReleaseArtifacts artifacts) : IReleaseOccurrenceInspector
{
    private const string PinKey = "etl.pipeline.v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string ClrDescriptor = typeof(ClrActivityDescriptor).FullName!;
    private static readonly string ResultAlias = TypeAliasConvention.CanonicalAlias(typeof(RunEtlResult));
    private static readonly (string LocalKey, string Handler) Resume = ReadResumeDeclaration();

    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.etl.native-workflows", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ClrActivity, RunEtl.ActivityType, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, PinKey, 1)
    });

    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) throw Unsupported("run_etl_site_unsupported");
            var context = sites.ReadNode(site, content);
            if (site.Input is not null || context.Node.ExecutableNodeId != site.Node) throw Invalid("run_etl_site_mismatch");
            ValidateContract(context);
            var node = context.Node;
            if (!node.InputBindings.TryGetValue(nameof(RunEtl.Pipeline), out var binding) ||
                binding.Source != RuntimeInputBindingSource.Literal)
                throw Unsupported("run_etl_dynamic_selection_unsupported");
            if (binding.LiteralValue is not { ValueKind: JsonValueKind.String } literal ||
                !SingleType(binding.TargetType, "String") || binding.Literal is not { Presence: ValuePresence.Present } envelope ||
                !SingleType(envelope.Type, "String")) throw Invalid("run_etl_selection_contract_invalid");
            ValidateSelectionConversion(binding);
            var token = literal.GetString();
            if (string.IsNullOrEmpty(token)) throw Invalid("run_etl_selection_invalid");
            if (!token.StartsWith("etl1:", StringComparison.Ordinal)) throw Unsupported("run_etl_selection_format_unsupported");
            EtlPublishedSelection selection;
            try { selection = EtlPublishedSelection.Parse(token); }
            catch (FormatException) { throw Invalid("run_etl_selection_invalid"); }

            var hasPin = node.Metadata.TryGetValue(PinKey, out var pin);
            EtlReleaseArtifact captured;
            if (hasPin)
            {
                if (pin!.Length > EtlArtifact.MaximumByteLength || StrictUtf8.GetByteCount(pin) > EtlArtifact.MaximumByteLength)
                    throw Invalid("run_etl_embedded_pin_too_large");
                var embedded = codec.DecodeOwnerArtifact(StrictUtf8.GetBytes(pin));
                captured = artifacts.Read(content, embedded.ArtifactId, context.Scope);
                if (!SameArtifact(embedded, captured.Artifact)) throw Invalid("run_etl_embedded_pin_mismatch");
            }
            else
            {
                if (site.Site.SiteKind == ReleaseRuntimeSiteKinds.Metadata || context.Workflow is not null)
                    throw Invalid("run_etl_embedded_pin_missing");
                captured = artifacts.Select(content, selection, context.Scope);
            }
            var artifact = captured.Artifact;
            if (artifact.DefinitionId != selection.DefinitionId || artifact.DefinitionVersion != selection.DefinitionVersion ||
                artifact.ContentHash != selection.ContentHash) throw Invalid("run_etl_selection_pin_mismatch");
            var genericEdge = CrosscheckGenericDependency(context, artifact);
            var canonicalFacet = hasPin ? site.Site.SiteKind == ReleaseRuntimeSiteKinds.Metadata : true;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified,
                edges: canonicalFacet && !genericEdge
                    ? [new(site.Member, new(EtlReleaseCodecs.MemberKind, artifact.ArtifactId), ReleaseEdgeRelation.Dependency, node.ExecutableNodeId)] : [],
                prerequisites: canonicalFacet
                    ? [new(PrerequisiteKey(site.Member, node.ExecutableNodeId), site.Member, "runtime-capability", "etl.native-workflows", "exact", "1")] : []));
        }
        catch (OperationCanceledException) { throw; }
        catch (EtlReleaseArtifactException exception)
        { return ValueTask.FromResult(Failed(exception.Code, exception.IsUnsupported ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid)); }
        catch (Exception exception) when (exception is ReleaseRuntimeArtifactException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("run_etl_retained_contract_invalid", ReleaseSemanticStatus.Invalid)); }

        ReleaseOccurrenceFacts Failed(string code, ReleaseSemanticStatus status) =>
            new(status, diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }

    private static void ValidateContract(ReleaseRuntimeNodeContext context)
    {
        var node = context.Node;
        if (node.ActivityType != RunEtl.ActivityType || node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.ClrActivity ||
            node.Descriptor.SchemaVersion != RuntimeActivityDescriptor.InitialSchemaVersion || !ClrPayload(node.Descriptor.Payload) ||
            node.IntrinsicKind is not null || node.IntrinsicVariable is not null || node.Structure is not null || node.ChildSlots.Count != 0 ||
            node.OutputCaptures.Count != 0 || node.InputBindings.Count != 1 || node.ExecutionPlacement != ActivityExecutionPlacement.Inline ||
            node.Metadata.ContainsKey(RuntimeMetadataKeys.StructuralFaultHandling) || node.Metadata.ContainsKey(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild) ||
            node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) || node.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion) ||
            node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) && executionType != "Action")
            throw Invalid("run_etl_leaf_shape_invalid");
        var contract = node.ActivityContract;
        if (contract is null || contract.ActivityTypeKey != RunEtl.ActivityType || contract.ContractVersion != node.ActivityTypeVersion ||
            contract.DescriptorKind != ClrDescriptor || !ClrPayload(contract.DescriptorPayload) ||
            contract.Activation.DescriptorKind != ClrDescriptor || contract.Activation.Capability != RunEtl.ActivityType ||
            contract.SideEffectProfile != SideEffectProfile.External || contract.SupportsBackgroundExecution || contract.Inputs.Count != 1 ||
            !contract.Inputs.TryGetValue(nameof(RunEtl.Pipeline), out var input) || !SingleType(input.Type, "String") ||
            !input.IsRequired || input.IsNullable || input.HasDefault || input.DefaultValue is not null || input.IsCredential ||
            input.ConnectionType is not null || input.IsStructureInput || !contract.Result.IsRequired ||
            !SingleType(contract.Result.Type, ResultAlias) || contract.Result.Projections.Count != 0 ||
            contract.Result.EffectiveSourceRepresentation != ValueRepresentation.TypedValue ||
            contract.Result.Policy != (ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result }) ||
            contract.Outcomes.Count != 2 || !contract.Outcomes.Contains(ActivityOutcomes.Done, StringComparer.Ordinal) ||
            !contract.Outcomes.Contains("BusinessError", StringComparer.Ordinal))
            throw Invalid("run_etl_activity_contract_invalid");
        ValidateResume(context);
    }

    private static void ValidateResume(ReleaseRuntimeNodeContext context)
    {
        var nodeId = context.Node.ExecutableNodeId;
        var targets = context.Workflow?.ResumeTargets ?? context.Template!.ResumeTargets;
        var owned = targets.Values.Where(target => target.ExecutableNodeId == nodeId).Take(2).ToArray();
        if (owned.Length != 1) throw Invalid("run_etl_resume_contract_invalid");
        WorkflowExecutableResumeTarget? selected;
        if (context.Workflow is { } workflow) selected = WorkflowExecutableResumeTarget.FindForNode(workflow, nodeId, Resume.LocalKey);
        else selected = targets.GetValueOrDefault(Resume.LocalKey) ?? (owned[0].LocalResumeTargetId == Resume.LocalKey ? owned[0] : null);
        if (selected is null || selected != owned[0] || selected.HandlerKey != Resume.Handler || selected.Metadata.Count != 0 ||
            selected.LocalResumeTargetId != Resume.LocalKey && !(selected.LocalResumeTargetId is null && selected.ResumeTargetId == Resume.LocalKey) ||
            !targets.TryGetValue(selected.ResumeTargetId, out var declared) || declared != selected)
            throw Invalid("run_etl_resume_contract_invalid");
    }

    private static void ValidateSelectionConversion(RuntimeInputBinding binding)
    {
        if (binding.ConversionRequest is not null) throw Unsupported("run_etl_selection_conversion_unsupported");
        if (binding.ConversionPlan is not { } plan) return;
        if (plan.Operation != ValueConversionOperation.Identity || plan.SourceRepresentation != ValueRepresentation.TextValue ||
            !SingleType(plan.SourceType, "String") || !SingleType(plan.TargetType, "String") ||
            plan.Options is not { ValueKind: JsonValueKind.Object } options || options.EnumerateObject().Any())
            throw Unsupported("run_etl_selection_conversion_unsupported");
    }

    private static bool CrosscheckGenericDependency(ReleaseRuntimeNodeContext context, EtlArtifact artifact)
    {
        if (context.Workflow is not { } workflow) return false;
        var dependencies = workflow.Dependencies.Where(item => item.DispatchNodeIds.Contains(context.Node.ExecutableNodeId, StringComparer.Ordinal)).Take(2).ToArray();
        if (dependencies.Length == 0) return false;
        if (dependencies.Length != 1 || dependencies[0].ArtifactId != artifact.ArtifactId || dependencies[0].ArtifactHash != artifact.ContentHash)
            throw Invalid("run_etl_generic_dependency_mismatch");
        // The generic owner keeps its own edge authority and currently refuses ETL-kind dependencies.
        return true;
    }

    private static bool SameArtifact(EtlArtifact left, EtlArtifact right) => left.FormatVersion == right.FormatVersion &&
        left.ArtifactId == right.ArtifactId && left.ContentHash == right.ContentHash && left.DefinitionId == right.DefinitionId &&
        left.DefinitionVersion == right.DefinitionVersion && JsonMarshal.GetRawUtf8Value(left.Pipeline).SequenceEqual(JsonMarshal.GetRawUtf8Value(right.Pipeline));

    private static bool SingleType(ValueTypeDescriptor type, string alias) => type.Alias == alias && type.CollectionKind == CollectionKind.Single &&
        type.Schema is null && type.SchemaVersion is null;
    private static bool ClrPayload(JsonElement payload) => payload.ValueKind == JsonValueKind.Object && payload.EnumerateObject().Count() == 1 &&
        payload.TryGetProperty("typeAlias", out var alias) && alias.ValueKind == JsonValueKind.String && alias.GetString() == RunEtl.ActivityType;

    private static (string, string) ReadResumeDeclaration()
    {
        var declaration = typeof(RunEtl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<ResumeTargetAttribute>()))
            .Single(item => item.Attribute is not null);
        return (declaration.Attribute!.ResumeTargetId, declaration.Method.Name);
    }

    private static string PrerequisiteKey(ReleaseMemberKey member, string node)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        Span<byte> scratch = stackalloc byte[1024];
        foreach (var value in new[] { member.Kind, member.Identity, node })
        {
            BinaryPrimitives.WriteInt32BigEndian(length, StrictUtf8.GetByteCount(value)); hash.AppendData(length);
            var encoder = StrictUtf8.GetEncoder(); var remaining = value.AsSpan();
            do
            {
                encoder.Convert(remaining, scratch, true, out var used, out var bytes, out var completed);
                hash.AppendData(scratch[..bytes]); remaining = remaining[used..];
                if (completed) break;
            } while (true);
        }
        return "etl.native/" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static EtlReleaseArtifactException Invalid(string code) => new(code);
    private static EtlReleaseArtifactException Unsupported(string code) => new(code, true);
}
