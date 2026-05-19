using Elsa.Activities.Design.Core.Contracts;
using System.Diagnostics;

namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// A descriptor of an activity type. It also provides a constructor to create instances of this type.
    /// </summary>
    [DebuggerDisplay("{TypeName}")]
    public sealed class ActivityDefinition : IActivityDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The fully qualified name of the activity type.
        /// </summary>
        public string FullyQualifiedName { get; set; } = null!;

        /// <summary>
        /// The namespace of the activity type.
        /// </summary>
        public string Namespace { get; set; } = null!;

        /// <summary>
        /// The name of the assembly in which this activity is located
        /// </summary>
        public string? AssemblyName { get; set; }

        /// <summary>
        /// The version of the assembly that hosts the activity
        /// </summary>
        public string? AssemblyVersion { get; set; }

        /// <summary>
        /// The name of this activity definition.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// The category of the activity type.
        /// </summary>
        public string Category { get; set; } = null!;

        /// <summary>
        /// The display name of the activity type.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// The description of the activity type.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The input properties of the activity type.
        /// </summary>
        public ICollection<IActivityPropertyDefinition> Inputs { get; init; } = [];

        /// <summary>
        /// The output properties of the activity type.
        /// </summary>
        public ICollection<IActivityPropertyDefinition> Outputs { get; init; } = [];

        /// <summary>
        /// The kind of activity.
        /// </summary>
        public ActivityKind Kind { get; set; } = ActivityKind.Action;

        /// <summary>
        /// The ports of the activity type.
        /// </summary>
        public ICollection<ActivityPortDefinition> Ports { get; set; } = [];


        /// <summary>
        /// Whether this activity type is selectable from activity pickers.
        /// </summary>
        public bool IsBrowsable { get; set; } = true; 


        IEnumerable<IActivityPropertyDefinition> IActivityDefinition.Inputs => Inputs;

        IEnumerable<IActivityPropertyDefinition> IActivityDefinition.Outputs => Outputs;

        IEnumerable<ActivityPortDefinition> IActivityDefinition.Ports => Ports;
    }
}
