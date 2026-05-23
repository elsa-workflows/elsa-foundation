namespace Elsa.Activities.Design.Core.Contracts
{
    public interface IActivityDefinition
    {
        /// <summary>
        /// Unique, immutable name that identifies the instance within its defined scope.
        /// </summary>
        /// <remarks>The name must be stable for the lifetime of the instance and unique within the
        /// relevant context (for example, a collection, namespace, or registry). Use this value as a key when
        /// consistent identification is required.</remarks>
        string UniqueName { get; }

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
        /// Whether this activity type is selectable from activity pickers.
        /// </summary>
        bool IsBrowsable { get; }
    }
}
