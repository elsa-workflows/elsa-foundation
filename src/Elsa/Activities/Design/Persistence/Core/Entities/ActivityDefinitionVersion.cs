using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

public sealed class ActivityDefinitionVersion(string version, string definitionId, string? inputsSource = null, string? outputsSource = null, string? designFacetsSource = null, ActivityExecutionType executionType = ActivityExecutionType.Action)
    : TenantEntity, IActivityDefinitionVersion
{
    /// <summary>
    /// Navigation property to the <see cref="ActivityDefinition"/>.
    /// </summary>
    public ActivityDefinition? Definition { get; set; }

    /// <summary>Author-controlled SemVer 2.0.0 string. Write-once.</summary>
    public string Version { get; init; } = version;

    /// <summary>
    /// Persistence-only normalised key whose ordinal ordering equals SemVer precedence
    /// (computed once at construction via <see cref="SemVer.ToSortKey(string)"/>). Drives DB-side
    /// ORDER BY for version listings and "latest version" resolution. A real CLR property, not an
    /// EF shadow — it is a domain shadow (§2.9.1): invisible to other domains via the interface,
    /// present on the entity for persistence. Write-once.
    /// </summary>
    public string SemVerSortKey { get; init; } = SemVer.ToSortKey(version);

    public string DefinitionId { get; init; } = definitionId;

    /// <summary>
    /// The descriptor type's <c>FullName</c> — the runtime construction registry's lookup key.
    /// Set by the reconciler (or the design API) from the descriptor it produced. The design domain
    /// never resolves it to a CLR type. Write-once — immutability enforced via
    /// <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration.
    /// </summary>
    public string DescriptorType { get; set; } = null!;

    /// <summary>
    /// Serialized JSON form of <see cref="DescriptorPayload"/>. A real string property on the entity
    /// (NOT an EF Core shadow property); post-insert immutability enforced via
    /// <c>PropertySaveBehavior.Throw</c>. The interface boundary does not expose it directly — it is a
    /// domain shadow; the round-trippable <see cref="DescriptorPayload"/> view is the exposed form.
    /// </summary>
    public string? DescriptorPayloadSource { get; set; }

    /// <summary>
    /// The descriptor payload as opaque JSON. <see cref="NotMappedAttribute"/>: EF Core does not
    /// persist this directly. Hydrated by the loading handler by parsing
    /// <see cref="DescriptorPayloadSource"/>; written back to that string by the saving handler. The
    /// design domain never deserializes it into a concrete descriptor type.
    /// </summary>
    [NotMapped]
    public JsonElement DescriptorPayload { get; set; }

    public string? InputsSource { get; set; } = inputsSource;

    public string? OutputsSource { get; set; } = outputsSource;

    public string? DesignFacetsSource { get; set; } = designFacetsSource;

    public ActivityExecutionType ExecutionType { get; init; } = executionType;

    [NotMapped]
    public IEnumerable<InputDefinition> Inputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<OutputDefinition> Outputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<ActivityDesignFacet> DesignFacets { get; set; } = [];

    /// <summary>
    /// Provenance source identifier — free-form string owned by the source module
    /// (e.g. "Json", "ClrDiscovery", "Workflow"). Write-once.
    /// </summary>
    public string SourceKind { get; set; } = null!;

    /// <summary>
    /// Source-side asset identity. Write-once.
    /// </summary>
    public string SourceId { get; set; } = null!;

    /// <summary>
    /// Write-once content hash of this version's projection, generated at construction by the
    /// version factory (<c>IActivityDefinitionHasher</c>). Under Model X, subsequent reconciliation
    /// passes that observe the same <c>(DefinitionId, Version)</c> compare their candidate's hash
    /// against this stored value to detect source-side breakage.
    /// </summary>
    public string? Hash { get; set; }

    IEnumerable<ActivityDesignFacet> IActivityDefinitionVersion.DesignFacets => DesignFacets;

    IActivityDefinition IActivityDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));

    /// <summary>
    /// Builds the persistence entity from any <see cref="IActivityDefinitionVersion"/> (typically a
    /// factory-produced read-model). The ctor recomputes <see cref="SemVerSortKey"/>. The
    /// <see cref="Definition"/> navigation is left unset — the write command wires the relationship
    /// via <see cref="DefinitionId"/>. Pass <paramref name="definitionId"/> to rebind the version to
    /// a different (e.g. already-persisted) definition while preserving the source's content
    /// <see cref="Hash"/> unchanged.
    /// </summary>
    public static ActivityDefinitionVersion From(IActivityDefinitionVersion source, string? definitionId = null) =>
        new(source.Version, definitionId ?? source.DefinitionId, executionType: source.ExecutionType)
        {
            Id = source.Id,
            DescriptorType = source.DescriptorType,
            DescriptorPayload = source.DescriptorPayload,
            Inputs = source.Inputs,
            Outputs = source.Outputs,
            DesignFacets = source.DesignFacets,
            SourceKind = source.SourceKind,
            SourceId = source.SourceId,
            Hash = source.Hash,
        };
}
