using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Contribution event published by <see cref="IActivityVersionReconciler"/> on each pass.
/// Source modules (JSON file, CLR discovery, workflow bridge, …) handle this event and
/// add the activity versions they currently observe to the carried collection.
/// </summary>
public sealed record OnActivityVersionsReconciling(ICollection<IActivityDefinitionVersion> Versions) : IDomainEvent;
