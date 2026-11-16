using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Events;

/// <summary>Sequential startup contribution of exact execution protocols before the registry freezes.</summary>
/// <remarks>Protocol sources feed one aggregating handler. Independent observers may subscribe without relying on handler order.</remarks>
public sealed class RuntimeExecutionProtocolsInitializing : IEvent
{
    public ICollection<RuntimeExecutionProtocolRegistration> Registrations { get; } = [];
}
