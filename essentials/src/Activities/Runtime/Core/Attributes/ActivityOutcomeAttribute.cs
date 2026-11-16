namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>Declares one successful authored outcome supported by an activity contract.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ActivityOutcomeAttribute(string key) : Attribute
{
    public string Key { get; } = !string.IsNullOrWhiteSpace(key)
        ? key
        : throw new ArgumentException("An activity outcome key is required.", nameof(key));
}
