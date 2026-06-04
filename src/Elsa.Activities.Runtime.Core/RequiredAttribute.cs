namespace Elsa.Activities.Runtime.Core;

/// <summary>
/// Marks an activity input or output property as required. The assembly reconciliation source
/// reads this attribute and sets <c>IsRequired</c> on the corresponding input/output definition.
/// This is the only property-level annotation the runtime carries — UI concerns (display name,
/// category, description) deliberately do not live on runtime activity types.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RequiredAttribute : Attribute;
