using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Kind-agnostic factory. Looks up the resolver via
/// <see cref="IActivityImplementationResolverRegistry"/>, resolves the CLR type, activates
/// via <see cref="ActivatorUtilities"/>. Application of input/output state to the
/// constructed activity is deferred to a follow-up — Unit B's contract specifies the
/// factory returns an <see cref="IActivity"/> for a CLR descriptor; the
/// <c>Input&lt;T&gt;</c>/<c>Output&lt;T&gt;</c> property wiring is not yet implemented.
/// </summary>
public sealed class ActivityFactory(
    IServiceProvider services,
    IActivityImplementationResolverRegistry resolvers)
    : IActivityFactory
{
    public ValueTask<IActivity> Create(
        IImplementationDescriptor descriptor,
        IEnumerable<InputState> inputs,
        IEnumerable<OutputState> outputs,
        CancellationToken cancellationToken)
    {
        // Resolve throws ActivityResolutionException if the kind has no registered resolver.
        var clrType = resolvers.Resolve(descriptor);
        var activity = (IActivity)ActivatorUtilities.CreateInstance(services, clrType);

        // Input/Output state wiring is out of scope for Unit B's contract acceptance.
        // The factory returns the constructed IActivity; binding of InputState/OutputState
        // to Input<T>/Output<T> properties lands when the runtime input/output pipeline
        // is wired (deferred — see plan §R6).
        _ = inputs;
        _ = outputs;

        return new(activity);
    }
}
