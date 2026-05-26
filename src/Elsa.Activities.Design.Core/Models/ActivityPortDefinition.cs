namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// Represents a port on an activity.
    /// </summary>
    public sealed record ActivityPortDefinition(string Name, string? DisplayName, ActivityPortType Type, bool IsBrowsable = true);
}
