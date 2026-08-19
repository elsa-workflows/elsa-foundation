using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Primitives.Extensions;
using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Derives a CLR activity's catalog category from the last dot-separated segment of its declaring
/// assembly's simple name — e.g. activities shipped in <c>Elsa.Runtime.Activities.Primitives</c> are
/// catalogued under <c>Primitives</c>. The convention groups every activity in an assembly under one
/// stable, author-controlled bucket (the assembly the author chose to ship them in) without
/// requiring a per-activity annotation. A feature that wants richer categorisation (e.g. a
/// type-level <c>[Category]</c> attribute) overrides this in isolation.
/// The raw segment is humanized for display (<c>ControlFlow</c> → <c>Control Flow</c>, <c>Bpmn</c> → <c>BPMN</c>).
/// A trailing <c>.Runtime</c> or <c>.Design</c> segment names the composition <em>plane</em>, not the catalog
/// bucket, and is skipped: <c>Elsa.Activities.ControlFlow.Runtime</c> ships Control Flow activities, not
/// "Runtime" ones (spec 151 T128 split these packages in two). The skip never consumes the domain segment
/// itself, so <c>Elsa.Activities.Runtime</c> — which <em>is</em> the runtime activity package — stays
/// <c>Runtime</c>.
/// </summary>
public sealed class ActivityTypeCategoryResolver : IActivityTypeCategoryResolver
{
    public string? Resolve(Type type, Assembly assembly)
    {
        var simpleName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(simpleName))
            return null;

        var segments = simpleName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Drop the plane suffix only when a real domain segment is left behind. Falling back to "Activities"
        // would mean the suffix was the domain (Elsa.Activities.Runtime), so in that case it is kept.
        if (segments is [.., var domain, "Runtime" or "Design"] &&
            !string.Equals(domain, "Activities", StringComparison.Ordinal))
            return domain.Humanize();

        return segments is [.., var last] ? last.Humanize() : null;
    }
}
