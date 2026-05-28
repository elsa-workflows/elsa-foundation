using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

public sealed class ActivityDefinitionVersion(int version, string definitionId, string? inputsSource = null, string? outputsSource = null, string? portsSource = null, ActivityExecutionType executionType = ActivityExecutionType.Action)
    : TenantEntity, IActivityDefinitionVersion
{
    /// <summary>
    /// Navigation property to the <see cref="ActivityDefinition"/>.
    /// </summary>
    public ActivityDefinition? Definition { get; set; }

    [Immutable]
    public int Version { get; init; } = version;

    [Immutable]
    public string DefinitionId { get; init; } = definitionId;

    /// <summary>
    /// Denormalised from the parent on insert. Immutable.
    /// </summary>
    [Immutable]
    public string ActivityTypeKey { get; set; } = null!;

    /// <summary>
    /// Registry lookup key matching <see cref="ImplementationDescriptor"/>.<c>Kind</c>.
    /// Immutable.
    /// </summary>
    [Immutable]
    public string ImplementationKind { get; set; } = null!;

    /// <summary>
    /// Serialized JSON form of <see cref="ImplementationDescriptor"/>. A real string property
    /// on the entity (NOT an EF Core shadow property) so the central <c>[Immutable]</c>
    /// scanner picks it up and the value follows the same lifecycle attributes as every
    /// other invariant-bearing field. The interface boundary <see cref="IActivityDefinitionVersion"/>
    /// does not expose it — the property is a "shadow" in our domain sense (invisible to
    /// other domains), distinct from EF Core's "shadow" (not on the CLR class).
    /// </summary>
    [Immutable]
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

    [Immutable]
    public string? InputsSource { get; set; } = inputsSource;

    [Immutable]
    public string? OutputsSource { get; set; } = outputsSource;

    [Immutable]
    public string? PortsSource { get; set; } = portsSource;

    [Immutable]
    public ActivityExecutionType ExecutionType { get; init; } = executionType;

    /// <summary>
    /// Immutable content hash of this version's projection, computed by
    /// <c>IActivityDefinitionHasher</c> at reconciliation time. Under Model X this is the
    /// only artefact carried forward between reconciliation passes: subsequent passes that
    /// observe the same <c>(DefinitionId, Version)</c> compare their candidate's hash
    /// against this stored value to detect source-side breakage.
    /// </summary>
    [Immutable]
    public string? ProvisioningHash { get; set; }

    [NotMapped]
    public IEnumerable<InputDefinition> Inputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<OutputDefinition> Outputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<ActivityPortDefinition> Ports { get; set; } = [];

    IEnumerable<ActivityPortDefinition> IActivityDefinitionVersion.Ports => Ports;

    IActivityDefinition IActivityDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
