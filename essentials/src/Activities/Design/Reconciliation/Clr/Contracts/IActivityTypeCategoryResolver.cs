using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Contracts;

/// <summary>
/// Resolves the catalog category for a CLR activity type. Like
/// <see cref="IActivityTypeVersionResolver"/>, this is a pure decision function over reflection-only
/// metadata so it can be overridden in isolation by a feature that wants a different categorisation
/// scheme.
/// </summary>
public interface IActivityTypeCategoryResolver
{
    /// <summary>
    /// Returns the category an activity should be catalogued under, or <c>null</c> when none can be
    /// derived.
    /// </summary>
    string? Resolve(Type type, Assembly assembly);
}
