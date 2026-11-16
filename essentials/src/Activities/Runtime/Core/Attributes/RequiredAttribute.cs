namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Marks an activity input or output property as required. The assembly reconciliation source
/// reads this attribute and sets <c>IsRequired</c> on the corresponding input/output definition.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RequiredAttribute : Attribute;
