namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Marks a contract declared outside this assembly that belongs to the operational-state provider family, so
/// <see cref="RuntimeOperationalStateStoreBackend"/> owns its registrations alongside the contracts declared here.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeOperationalStateContractAttribute : Attribute;
