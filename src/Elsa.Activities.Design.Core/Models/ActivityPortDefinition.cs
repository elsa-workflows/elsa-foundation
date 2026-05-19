namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// Represents a port on an activity.
    /// </summary>
    public class ActivityPortDefinition
    {
        /// <summary>
        /// Gets or sets the name of the port.
        /// </summary>
        public string Name { get; set; } = default!;

        /// <summary>
        /// Gets or sets the display name of the port.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the type of the port.
        /// </summary>
        public ActivityPortType Type { get; set; }

        /// <summary>
        /// Gets or sets the visibility of the port.
        /// </summary>
        public bool IsBrowsable { get; set; } = true;
    }
}
