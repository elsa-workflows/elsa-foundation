using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts
{
    public interface IActivityDefinition
    {
        string Id { get; }

        string Name { get; }

        /// <summary>
        /// The category of the activity type.
        /// </summary>
        string Category { get; }

        /// <summary>
        /// The display name of the activity type.
        /// </summary>
        string? DisplayName { get; }

        /// <summary>
        /// The description of the activity type.
        /// </summary>
        string? Description { get; }

        /// <summary>
        /// The fully qualified name of the activity type.
        /// </summary>
        string FullyQualifiedName { get; }

        /// <summary>
        /// The namespace of the activity type.
        /// </summary>
        string Namespace { get; }

        /// <summary>
        /// The name of the assembly in which this activity is located
        /// </summary>
        string? AssemblyName { get; }

        /// <summary>
        /// The version of the assembly in which this activity is located
        /// </summary>
        string? AssemblyVersion { get; }


        IEnumerable<IActivityPropertyDefinition> Inputs { get; }

        IEnumerable<IActivityPropertyDefinition> Outputs { get; }

        IEnumerable<ActivityPortDefinition> Ports { get; }

        /// <summary>
        /// The kind of activity.
        /// </summary>
        ActivityKind Kind { get; }

        /// <summary>
        /// Whether this activity type is selectable from activity pickers.
        /// </summary>
        bool IsBrowsable { get; }        
    }
}
