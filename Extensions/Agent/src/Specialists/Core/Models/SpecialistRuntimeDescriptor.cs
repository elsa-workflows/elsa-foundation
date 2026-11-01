using System.Text.Json;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>Portable immutable execution material; it contains no client, credential or mutable draft reference.</summary>
public sealed record SpecialistRuntimeDescriptor(
    string Instructions,
    SpecialistModelReference Model,
    JsonElement Settings,
    JsonElement InputSchema,
    JsonElement ResultSchema,
    string CapabilitiesFingerprint,
    string SchemaProfile,
    SpecialistTurnLimits Limits,
    bool AllowContinuity,
    string? DefinitionId = null,
    string? DefinitionVersionId = null,
    string? Version = null,
    string? TemplateHash = null,
    string? SourceReferenceId = null)
{
    /// <summary>Explicit native invocation contract profile. Null retains the legacy fresh-only public boundary.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InvocationControlsProfile { get; init; }
}

/// <summary>Native persisted provenance supplied by the runtime, never an authorization grant from a caller.</summary>
public sealed record SpecialistInvocationIdentity(
    string? TenantId,
    string PersistencePartition,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string OperationKey,
    string ArtifactId,
    string ArtifactHash,
    string DefinitionVersionId,
    string TemplateHash);

/// <summary>Current model metadata; its fingerprint changes whenever effective support changes.</summary>
public sealed record SpecialistModelCapabilities(string Fingerprint, JsonElement SettingsSchema, bool SupportsJsonSchema, int MaximumOutputTokens);
