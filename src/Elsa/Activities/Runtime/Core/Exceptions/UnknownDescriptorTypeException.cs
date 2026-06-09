namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Thrown at construction time when no <c>IActivityConstructor</c> is registered for a descriptor
/// type — typically because the feature that owns the descriptor type is not installed in this host.
/// A domain failure (Elsa §E2.6), not a system fault: cataloguing and reading the row are unaffected;
/// only construction of an instance fails.
/// </summary>
public sealed class UnknownDescriptorTypeException(string descriptorType)
    : Exception($"No activity constructor is registered for descriptor type '{descriptorType}'. The feature that owns this descriptor type may not be installed.")
{
    public string DescriptorType { get; } = descriptorType;
}
