using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Querying;

/// <summary>
/// Admits OpenIddict's generic query delegates only after their shape has a
/// declared bounded storage route. The current manifest has no generic route,
/// so every delegate is rejected before it is invoked or a provider is opened.
/// </summary>
public static class OpenIddictGroundworkGenericQueryTranslator
{
    /// <summary>
    /// Validates a generic count delegate before a store can acquire a provider session.
    /// </summary>
    public static void RequireCountRoute<TRecord, TResult>(
        Func<IQueryable<TRecord>, IQueryable<TResult>> query,
        string operation)
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(query);
        Reject(operation);
    }

    /// <summary>
    /// Validates a stateful generic get/list delegate before a store can acquire a provider session.
    /// </summary>
    public static void RequireStatefulRoute<TRecord, TState, TResult>(
        Func<IQueryable<TRecord>, TState, IQueryable<TResult>> query,
        string operation)
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(query);
        Reject(operation);
    }

    private static void Reject(string operation) => throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery(operation);
}
