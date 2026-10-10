using Microsoft.AspNetCore.Routing.Template;

namespace Elsa.Http.Services;

/// <summary>
/// Orders endpoint route templates by descending specificity so a "first match wins" enumeration resolves
/// overlapping templates deterministically (spec 089 follow-up, issue #592 item 1). Backed by an unordered
/// container, <c>orders/list</c> vs <c>orders/{id}</c> could match in either order — a request for
/// <c>orders/list</c> might bind the parameter route, and the outcome could flip across republish or restart.
/// This comparer makes the more specific template sort first, mirroring ASP.NET route precedence: per segment a
/// literal outranks a parameter outranks a catch-all; if all shared segments tie, the shorter template
/// (fewer segments) outranks the longer; ordinal template text is the final, total-order tiebreak.
/// </summary>
/// <remarks>
/// The route table precompiles a <see cref="TemplateMatcher"/> per route at refresh, so it already holds the
/// parsed template; <see cref="Compare(RouteTemplate,RouteTemplate)"/> takes that parsed form to avoid a re-parse.
/// Comparison is on the parsed shape, so it is robust to case and inline constraints/defaults: two templates that
/// differ only in a parameter's constraint body order by their segment shape, not their text.
/// </remarks>
internal static class RouteTemplateSpecificity
{
    /// <summary>An <see cref="IComparer{T}"/> over raw template strings (parses each side). More specific sorts first.</summary>
    public static IComparer<string> StringComparer { get; } = Comparer<string>.Create(
        (left, right) => Compare(TemplateParser.Parse(left), TemplateParser.Parse(right)) is var shape && shape != 0
            ? shape
            : System.StringComparer.Ordinal.Compare(left, right));

    /// <summary>Compares two parsed templates by segment-shape specificity, with ordinal text as the total-order tiebreak.</summary>
    public static int Compare(RouteTemplate left, RouteTemplate right)
    {
        var leftSegments = left.Segments;
        var rightSegments = right.Segments;
        var shared = Math.Min(leftSegments.Count, rightSegments.Count);

        for (var i = 0; i < shared; i++)
        {
            var rank = Rank(leftSegments[i]).CompareTo(Rank(rightSegments[i]));
            if (rank != 0)
                return rank;
        }

        if (leftSegments.Count != rightSegments.Count)
            return leftSegments.Count.CompareTo(rightSegments.Count);

        // Same shape: order by the raw template text so equal-shape templates get a deterministic,
        // insertion-order-independent position.
        return System.StringComparer.Ordinal.Compare(left.TemplateText, right.TemplateText);
    }

    /// <summary>
    /// A lower rank is more specific: a purely literal segment (0) outranks one carrying a bound parameter (1),
    /// which outranks a catch-all (<c>{*rest}</c>, 2). A segment with mixed literal+parameter parts (e.g.
    /// <c>file.{ext}</c>) counts as a parameter segment.
    /// </summary>
    private static int Rank(TemplateSegment segment)
    {
        if (segment.Parts.Any(part => part.IsCatchAll))
            return 2;

        if (segment.Parts.Any(part => part.IsParameter))
            return 1;

        return 0;
    }
}
