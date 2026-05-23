using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

public sealed class ActivityDefinitionVersion(TypeInformation typeInfo, int version, string definitionId, string? inputsSource = null, string? outputsSource = null, string? portsSource = null, ActivityKind kind = ActivityKind.Action) 
    : Entity, IActivityDefinitionVersion
{
    /// <summary>
    /// Type information: asssembly name, version, namespace, and fully qualified type name
    /// </summary>
    [Immutable]
    public TypeInformation TypeInfo { get; } = typeInfo;

    /// <summary>
    /// Navigation property to the <see cref="ActivityDefinition"/>
    /// </summary>
    public ActivityDefinition? Definition { get; set; }

    [Immutable]
    public int Version { get; } = version;

    [Immutable]
    public string DefinitionId { get; } = definitionId;

    [Immutable]
    public string? InputsSource { get; set; } = inputsSource;

    [Immutable]
    public string? OutputsSource { get; set; } = outputsSource;

    [Immutable]
    public string? PortsSource { get; set; } = portsSource;

    /// <summary>
    /// The kind of activity.
    /// </summary>
    [Immutable]
    public ActivityKind Kind { get; } = kind;

    /// <summary>
    /// The input properties of the activity type.
    /// </summary>
    [NotMapped]
    public IEnumerable<InputDefinition> Inputs { get; set; } = [];

    /// <summary>
    /// The output properties of the activity type.
    /// </summary>
    [NotMapped]
    public IEnumerable<OutputDefinition> Outputs { get; set; } = [];

    /// <summary>
    /// The ports of the activity type.
    /// </summary>
    [NotMapped]
    public IEnumerable<ActivityPortDefinition> Ports { get; set; } = [];

    IEnumerable<IInputDefinition> IActivityDefinitionVersion.Inputs => Inputs;

    IEnumerable<IOutputDefinition> IActivityDefinitionVersion.Outputs => Outputs;

    IEnumerable<ActivityPortDefinition> IActivityDefinitionVersion.Ports => Ports;

    IActivityDefinition IActivityDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
