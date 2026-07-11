namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Marks an activity as capable of starting a workflow from an external stimulus.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TriggerActivityAttribute : Attribute;
