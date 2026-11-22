using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// The activity authoring catalog as the designer offers it to the current actor in the current tenant, for consumers
/// outside the Activities Design API (such as Weaver) that may couple only to <c>.Core</c> contracts.
/// </summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context: exactly one
/// implementation is installed, and a second registration fails startup. Consumers must fail closed when no
/// implementation or several implementations are installed. The default, in the Activities Design API, applies the
/// designer's own gate (the actor's activity design read permission in the request tenant) and lists every catalog
/// entry, addable or not, with the availability the designer reports. It accepts no principal, permission string or
/// tenant from the caller.
/// </remarks>
public interface IActivityAuthoringCatalog
{
    /// <summary>Whether the current actor may read the authoring catalog in the current tenant. Reads no catalog entries.</summary>
    /// <exception cref="ActivityAuthoringCatalogUnavailableException">An infrastructure failure prevented the decision.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the catalog for the current actor and tenant, or reports that the actor may not read it.</summary>
    /// <returns>A denied result carries no entries.</returns>
    /// <exception cref="ActivityAuthoringCatalogUnavailableException">An infrastructure failure prevented the read.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The catalog owner could not decide or read; the original cause is kept as <see cref="Exception.InnerException"/>.</summary>
/// <remarks>The message is fixed and discloses no cause.</remarks>
public sealed class ActivityAuthoringCatalogUnavailableException(Exception? innerException = null)
    : Exception(FixedMessage, innerException)
{
    /// <summary>The stable code of the unavailable outcome.</summary>
    public const string UnavailableCode = "activities.catalog.unavailable";

    /// <summary>The fixed, cause-free message.</summary>
    public const string FixedMessage = "The activity authoring catalog could not be read.";
}
