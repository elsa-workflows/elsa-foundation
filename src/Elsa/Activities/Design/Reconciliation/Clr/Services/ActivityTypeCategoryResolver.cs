using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Derives a CLR activity's catalog category from the last dot-separated segment of its declaring
/// assembly's simple name — e.g. activities shipped in <c>Elsa.Runtime.Activities.Primitives</c> are
/// catalogued under <c>Primitives</c>. The convention groups every activity in an assembly under one
/// stable, author-controlled bucket (the assembly the author chose to ship them in) without
/// requiring a per-activity annotation. A feature that wants richer categorisation (e.g. a
/// type-level <c>[Category]</c> attribute) overrides this in isolation.
/// </summary>
public sealed class ActivityTypeCategoryResolver : IActivityTypeCategoryResolver
{
    public string? Resolve(Type type, Assembly assembly)
    {
        var simpleName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(simpleName))
            return null;

        var segments = simpleName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is [.., var last] ? last : null;
    }
}
