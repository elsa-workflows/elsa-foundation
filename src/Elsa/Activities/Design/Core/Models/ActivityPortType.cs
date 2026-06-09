namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// The type of a port.
/// </summary>
public enum ActivityPortType
{
    /// <summary>
    /// A port that is embedded in the activity.
    /// </summary>
    Embedded,

    /// <summary>
    /// A flow port.
    /// </summary>
    Flow
}