using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Single construction entry point — dispatch + lifecycle orchestration only. Resolves the
/// <see cref="IActivityConstructor"/> registered for the <paramref name="descriptorType"/> and
/// delegates; performs no type resolution or argument binding itself (those are the constructor's
/// job, per kind). A <b>replacement</b> contract: one swappable construction service per host.
/// Carries no Design dependency (Elsa §E2.2).
/// </summary>
public interface IActivityFactory
{
    /// <summary>
    /// Construct an <see cref="IActivity"/> from a persisted descriptor + author-filled state.
    /// </summary>
    /// <param name="descriptorType">The descriptor type's <c>FullName</c> — the registry key.</param>
    /// <param name="payload">The opaque serialized descriptor; the owning constructor deserializes it.</param>
    /// <param name="inputs">Author-filled input arguments, keyed by reference key.</param>
    /// <param name="outputs">Author-filled output arguments, keyed by reference key.</param>
    /// <exception cref="Exceptions.UnknownDescriptorTypeException">No constructor registered for <paramref name="descriptorType"/>.</exception>
    ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default);
}
