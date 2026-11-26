namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity input is a service-connection requirement: it holds a
/// <c>ConnectionReference</c> (name and type only) and is authored with the Connection picker.
/// This is a distinct input kind from <see cref="ActivityInputAttribute"/>; pair it with
/// <see cref="ActivityInputAttribute"/> so the existing catalog, compile and hydration pipeline
/// carry the input, while this attribute supplies the required connection type and capabilities.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ActivityConnectionAttribute : Attribute
{
    /// <summary>The connection type the bound connection must have, for example <c>http</c>.</summary>
    public required string ConnectionType { get; init; }
}
