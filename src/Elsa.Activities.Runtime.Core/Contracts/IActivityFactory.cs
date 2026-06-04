using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Kind-agnostic activity construction dispatcher. Resolves the matching
/// <see cref="IActivityImplementationResolver"/> for the descriptor's <c>Kind</c>, asks the
/// resolver for the CLR type, activates it, and applies the supplied input/output states.
/// One factory per host.
/// </summary>
public interface IActivityFactory
{
    /// <summary>
    /// Construct an <see cref="IActivity"/> from a descriptor + filled-in input/output state.
    /// Throws <c>ActivityResolutionException</c> if no resolver is registered for the
    /// descriptor's <c>Kind</c> — a domain failure (Elsa §E2.6.1), not a system failure.
    /// </summary>
    ValueTask<IActivity> Create(
        IImplementationDescriptor descriptor,
        IEnumerable<InputState> inputs,
        IEnumerable<OutputState> outputs,
        CancellationToken cancellationToken);
}
