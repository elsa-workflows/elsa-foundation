using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Single construction entry point — dispatch + lifecycle orchestration only. Resolves the
/// <see cref="IActivityConstructor"/> registered for the descriptor's consumer key/schema and
/// delegates; performs no type resolution or argument binding itself (those are the constructor's
/// job, per kind). A <b>replacement</b> contract: one swappable construction service per host.
/// Carries no Design dependency (Elsa §E2.2).
/// </summary>
public interface IActivityFactory
{
    /// <summary>
    /// Construct an <see cref="IActivity"/> from a persisted descriptor + author-filled state.
    /// </summary>
    ValueTask<IActivity> Create(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument>? inputs,
        IReadOnlyDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default);
}
