namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>Declares one ordered, labeled allowable value for an activity input.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ActivityInputOptionAttribute(string label, object value) : Attribute
{
    public string Label { get; } = label;
    public object Value { get; } = value;
}
