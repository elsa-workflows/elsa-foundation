using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities
{
    /// <summary>
    /// A definition of an activity type.                                                                                                                                                                                
    /// </summary>
    public sealed class ActivityDefinition : Entity, IActivityDefinition
    {     
        /// <summary>
        /// The unique name of this activity definition.
        /// </summary>
        [Immutable]
        public string UniqueName { get; set; } = null!;

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
        /// Whether this activity type is selectable from activity pickers.
        /// </summary>
        public bool IsBrowsable { get; set; } = true;
    }
}
