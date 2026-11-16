namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Marks a type whose registrations are the Runtime implementation's replaceable defaults: a store registered as the
/// default implementation, or a composition root whose factory registrations are defaults. The provider backends in
/// this assembly cannot reference the implementation, so they recognize its defaults by this marker; a renamed or moved
/// default keeps its marker, where a type-name string would silently stop matching.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RuntimeDefaultRegistrationAttribute : Attribute;
