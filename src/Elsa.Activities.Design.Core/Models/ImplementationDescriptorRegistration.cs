namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Pairs an <c>ImplementationKind</c> string with the CLR descriptor type used to
/// (de)serialize the persisted JSON payload. Contributed at startup by the module that
/// owns the kind.
/// </summary>
public sealed record ImplementationDescriptorRegistration(string Kind, Type DescriptorType);
