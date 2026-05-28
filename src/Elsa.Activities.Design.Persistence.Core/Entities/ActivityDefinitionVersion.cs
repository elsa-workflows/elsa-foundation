using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

public sealed class ActivityDefinitionVersion(int version, string definitionId, string? inputsSource = null, string? outputsSource = null, string? portsSource = null, ActivityKind kind = ActivityKind.Action)
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
    /// Polymorphic descriptor. <see cref="NotMappedAttribute"/> — not a CLR-mapped column.
    /// The persisted form is the EF shadow column named <c>ImplementationDescriptor</c>
    /// (declared with <c>PropertySaveBehavior.Throw</c> in the EF Core configuration).
    /// The saving handler serializes this property into the shadow; the loading handler
    /// deserializes the shadow into this property using the registry's kind→type lookup.
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
    public ActivityKind Kind { get; init; } = kind;

    [NotMapped]
    public IEnumerable<InputDefinition> Inputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<OutputDefinition> Outputs { get; set; } = [];

    [NotMapped]
    public IEnumerable<ActivityPortDefinition> Ports { get; set; } = [];

    IEnumerable<ActivityPortDefinition> IActivityDefinitionVersion.Ports => Ports;

    IActivityDefinition IActivityDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
