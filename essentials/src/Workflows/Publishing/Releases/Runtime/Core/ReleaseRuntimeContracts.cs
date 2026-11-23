using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Core;

/// <summary>Source content namespace, never target placement or proof of publisher authenticity.</summary>
public sealed record ReleaseRuntimeContentScope(string Authority, string? TenantId, string? Partition);
public sealed record ReleaseRuntimeWorkflow(ReleaseRuntimeContentScope Scope, WorkflowExecutable Executable);
public sealed record ReleaseRuntimeTemplate(ReleaseRuntimeContentScope Scope, ExecutableActivityTemplate Template);
public sealed record ReleaseRuntimeModel(ReleaseRuntimeContentScope Scope, RuntimeModelMaterial Model);

/// <summary>Read-only selected source-version association. Artifact is the retained producer pin, independent of selected version labels.</summary>
public sealed record ReleaseWorkflowSelection(ReleaseRuntimeContentScope Scope, string DefinitionId, string DefinitionVersionId,
    string Version, string StateSource, string SourceKind, string SourceId, string? SourceVersion,
    string SourceReferenceId, WorkflowExecutableReferenceScope ReferenceScope, WorkflowExecutableIdentity Artifact);

/// <summary>Portable catalog association, not a Design entity or mutable target publication. Opaque contract/provider inspection remains mandatory.</summary>
public sealed class ReleaseActivityVersion(ReleaseRuntimeContentScope scope, string definitionId, string versionId, string version,
    string activityTypeKey, string resolutionKind, JsonElement contract, string providerKey, string providerSchemaVersion,
    JsonElement providerPayload, string compilerFingerprint, string templateId, string templateHash, string sourceReferenceId,
    string sourceKind, string sourceId)
{
    [JsonConstructor]
    public ReleaseActivityVersion(ReleaseRuntimeContentScope scope, string definitionId, string versionId, string version,
        string activityTypeKey, string resolutionKind, JsonElement contract, string providerKey, string providerSchemaVersion,
        JsonElement providerPayload, string compilerFingerprint, string templateId, string templateHash, string sourceReferenceId,
        string sourceKind, string sourceId, ReleaseActivityPublicationEvidence? publication)
        : this(scope, definitionId, versionId, version, activityTypeKey, resolutionKind, contract, providerKey, providerSchemaVersion,
            providerPayload, compilerFingerprint, templateId, templateHash, sourceReferenceId, sourceKind, sourceId) => Publication = publication;
    public ReleaseRuntimeContentScope Scope { get; } = scope;
    public string DefinitionId { get; } = definitionId;
    public string VersionId { get; } = versionId;
    public string Version { get; } = version;
    public string ActivityTypeKey { get; } = activityTypeKey;
    public string ResolutionKind { get; } = resolutionKind;
    public JsonElement Contract { get; } = contract.Clone();
    public string ProviderKey { get; } = providerKey;
    public string ProviderSchemaVersion { get; } = providerSchemaVersion;
    public JsonElement ProviderPayload { get; } = providerPayload.Clone();
    public string CompilerFingerprint { get; } = compilerFingerprint;
    public string TemplateId { get; } = templateId;
    public string TemplateHash { get; } = templateHash;
    public string SourceReferenceId { get; } = sourceReferenceId;
    public string SourceKind { get; } = sourceKind;
    public string SourceId { get; } = sourceId;
    /// <summary>Codec profile 2 original publication evidence. Missing in legacy profile 1; never grants target visibility.</summary>
    public ReleaseActivityPublicationEvidence? Publication { get; }
}

/// <summary>Original full Provider fragment and publication metadata; physical owner tenant is independent of acquisition Scope.</summary>
/// <remarks>OwnerTenantKnown distinguishes unknown from known-null/global. It is inert provenance, never a target visibility grant.</remarks>
public sealed class ReleaseActivityPublicationEvidence(bool ownerTenantKnown, string? ownerTenantId, string contractJson, string providerJson, JsonElement metadata)
{
    public bool OwnerTenantKnown { get; } = ownerTenantKnown;
    public string? OwnerTenantId { get; } = ownerTenantId;
    public string ContractJson { get; } = contractJson;
    public string ProviderJson { get; } = providerJson;
    public JsonElement Metadata { get; } = metadata.Clone();
}

/// <summary>Copied owner-codec bytes and their descriptor; original owner IDs are preserved.</summary>
public sealed class ReleaseEncodedRuntimeMember(ReleaseBundleMember descriptor, ReleaseOwnerVersion? ownerVersion, ReadOnlyMemory<byte> payload)
{
    private readonly byte[] _payload = payload.ToArray();
    public ReleaseBundleMember Descriptor { get; } = descriptor;
    public ReleaseOwnerVersion? OwnerVersion { get; } = ownerVersion;
    public byte[] GetPayloadBytes() => (byte[])_payload.Clone();
}

/// <summary>Replacement contract: one fixed, bounded owner wire codec per shell; never compiles, hydrates arbitrary CLR objects or resolves external stores.</summary>
public interface IReleaseRuntimeArtifactCodec
{
    ReleaseEncodedRuntimeMember EncodeWorkflow(ReleaseRuntimeWorkflow value, ReleaseBundleLimits? limits = null);
    ReleaseEncodedRuntimeMember EncodeTemplate(ReleaseRuntimeTemplate value, ReleaseBundleLimits? limits = null);
    ReleaseEncodedRuntimeMember EncodeModel(ReleaseRuntimeModel value, ReleaseBundleLimits? limits = null);
    ReleaseEncodedRuntimeMember EncodeWorkflowSelection(ReleaseWorkflowSelection value, ReleaseBundleLimits? limits = null);
    ReleaseEncodedRuntimeMember EncodeActivityVersion(ReleaseActivityVersion value, ReleaseBundleLimits? limits = null);
    ReleaseRuntimeWorkflow DecodeWorkflow(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
    ReleaseRuntimeTemplate DecodeTemplate(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
    /// <summary>Decodes neutral material and its pin; this recognizes no model protocol/profile or inner dependency semantics.</summary>
    ReleaseRuntimeModel DecodeModel(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
    ReleaseWorkflowSelection DecodeWorkflowSelection(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
    ReleaseActivityVersion DecodeActivityVersion(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
}

/// <summary>Read-only captured owner input. Inspectors must not mutate nested legacy model collections. Workflow and Template are mutually exclusive.</summary>
public sealed class ReleaseRuntimeNodeContext(ReleaseContentMember member, ReleaseRuntimeContentScope scope, ExecutableNode node,
    WorkflowExecutable? workflow, ExecutableActivityTemplate? template)
{
    public ReleaseContentMember Member { get; } = member;
    public ReleaseRuntimeContentScope Scope { get; } = scope;
    public ExecutableNode Node { get; } = node;
    public WorkflowExecutable? Workflow { get; } = workflow;
    public ExecutableActivityTemplate? Template { get; } = template;
}

/// <summary>Replacement contract: one captured-site reader per shell. Reads indexed bundle content only, without source/target fallback. Returned typed views are read-only inspector inputs.</summary>
public interface IReleaseRuntimeSiteReader
{
    ReleaseRuntimeNodeContext ReadNode(ReleaseSemanticSite site, IReleaseContentReader content);
    ReleaseRuntimeModel ReadModel(ReleaseSemanticSite site, IReleaseContentReader content);
    ReleaseRuntimeTemplate ReadTemplate(ReleaseMemberKey member, IReleaseContentReader content);
    ReleaseActivityVersion ReadActivityVersion(ReleaseMemberKey member, IReleaseContentReader content);
}

public static class ReleaseRuntimeCodecs
{
    public const string WorkflowKind = "workflow-executable";
    public const string TemplateKind = "activity-template";
    public const string ModelKind = "runtime-model";
    public const string SelectionKind = "workflow-selection";
    public const string ActivityVersionKind = "activity-version";
    public const string Workflow = "elsa.workflow-executable";
    public const string Template = "elsa.activity-template";
    public const string Model = "elsa.runtime-model-material";
    public const string Selection = "elsa.workflow-selection";
    public const string ActivityVersion = "elsa.activity-version";
    public const int Version = 1;
}

/// <summary>Schema 1 is the site envelope; owner descriptor/structure versions remain exact strings in captured material.</summary>
public static class ReleaseRuntimeSiteKinds
{
    public const string Descriptor = "runtime.descriptor";
    public const string ClrActivity = "runtime.clr-activity";
    public const string Structure = "runtime.structure";
    public const string Metadata = "runtime.node-metadata";
    public const string Placement = "runtime.placement";
    public const string Expression = "runtime.expression";
    public const string Conversion = "runtime.conversion";
    public const string Provider = "runtime.activity-provider";
    public const string PublicContract = "runtime.activity-contract";
    public const string TemplateOverlay = "runtime.template-overlay";
    public const string Operator = "runtime.operator-policy";
    public const string ModelProtocol = "runtime.model-protocol";
    public const string Compatibility = "runtime.compatibility";
    public const string InputMetadata = "runtime.input-metadata";
    public const string ChildControlConsumer = "runtime.child-control-consumer";
}

/// <summary>Stable value-free owner-codec refusal; retained bytes and source identifiers are never included in exception text.</summary>
public sealed class ReleaseRuntimeArtifactException(string code) : Exception($"Runtime release artifact was refused ({code}).")
{
    public string Code { get; } = code;
}
