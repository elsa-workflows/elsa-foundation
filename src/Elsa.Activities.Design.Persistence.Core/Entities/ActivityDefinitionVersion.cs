using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

public sealed class ActivityDefinitionVersion(string version, string definitionId, string? inputsSource = null, string? outputsSource = null, string? portsSource = null, ActivityExecutionType executionType = ActivityExecutionType.Action)
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
    /// Registry lookup key matching <see cref="ImplementationDescriptor"/>.<c>Kind</c>.
    /// Write-once — immutability enforced via <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration.
    /// </summary>
    public string ImplementationKind { get; set; } = null!;

    /// <summary>
    /// Serialized JSON form of <see cref="ImplementationDescriptor"/>. A real string property
    /// on the entity (NOT an EF Core shadow property); post-insert immutability enforced via
    /// <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration.
    /// The interface boundary <see cref="IActivityDefinitionVersion"/>
    /// does not expose it — the property is a "shadow" in our domain sense (invisible to
    /// other domains), distinct from EF Core's "shadow" (not on the CLR class).
    /// </summary>
    public string? ImplementationDescriptorPayload { get; set; }

    /// <summary>
    /// Polymorphic descriptor — the rich projection of
    /// <see cref="ImplementationDescriptorPayload"/>. <see cref="NotMappedAttribute"/>:
    /// EF Core does not persist this property directly. Hydrated by the loading handler
    /// from the payload + the descriptor registry's kind→type lookup; written back to the
    /// payload by the saving handler.
    /// </summary>
    [NotMapped]
    public IImplementationDescriptor ImplementationDescriptor { get; set; } = null!;

    public string? InputsSource { get; set; } = inputsSource;

    public string? OutputsSource { get; set; } = outputsSource;

    public string? PortsSource { get; set; } = portsSource;

    public ActivityExecutionType ExecutionType { get; init; } = executionType;

    [NotMapped]
    public IEnumerable<InputDefinition> Inputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<OutputDefinition> Outputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<ActivityPortDefinition> Ports { get; set; } = [];

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
    /// First-provisioning timestamp. Write-once.
    /// </summary>
    public DateTimeOffset ReconciledAt { get; set; }

    /// <summary>
    /// Identity that produced this row. Write-once.
    /// </summary>
    public string? ReconciledBy { get; set; }

    /// <summary>
    /// Write-once content hash of this version's projection, computed by
    /// <c>IActivityDefinitionHasher</c> at reconciliation time. Under Model X this is the
    /// only artefact carried forward between reconciliation passes: subsequent passes that
    /// observe the same <c>(DefinitionId, Version)</c> compare their candidate's hash
    /// against this stored value to detect source-side breakage.
    /// </summary>
    public string? ReconcilliationHash { get; set; }

    IEnumerable<ActivityPortDefinition> IActivityDefinitionVersion.Ports => Ports;

    IActivityDefinition IActivityDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
