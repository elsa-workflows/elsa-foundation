using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Bridges Runtime's activated durable driver registry into provider-neutral Activity authoring
/// capabilities without introducing a Design-to-Runtime dependency.
/// </summary>
public sealed class RuntimeActivityContractStorageDriverProvider(
    IRuntimeDurableValueStorageDriverRegistry storageDrivers) : IActivityContractStorageDriverProvider
{
    public IReadOnlyCollection<string> DriverKeys => storageDrivers.DriverKeys;
}
