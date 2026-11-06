using System.Globalization;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Workflows.Dashboard.Queries;

internal static class WorkflowQueryValues
{
    public static DashboardValue Resolve(DashboardValue value, DashboardQueryContext query) =>
        value.Parameter is { } name ? query.Parameters[name] : value;

    public static bool Matches(DashboardPredicate? predicate, Func<string, DashboardValue> lookup, DashboardQueryContext query)
    {
        if (predicate is null) return true;
        if (predicate.All is not null) return predicate.All.All(child => Matches(child, lookup, query));
        if (predicate.Any is not null) return predicate.Any.Any(child => Matches(child, lookup, query));
        var actual = lookup(predicate.Field!);
        if (predicate.Operator == "isNull") return actual.Type == "null";
        if (predicate.Operator == "isNotNull") return actual.Type != "null";
        if (predicate.Operator is "in" or "notIn")
        {
            var found = predicate.Values!.Any(value => Compare(actual, Resolve(value, query)) == 0);
            return predicate.Operator == "in" ? found : !found;
        }
        var expected = Resolve(predicate.Value!, query);
        if (predicate.Operator is "contains" or "startsWith")
            return actual.Literal is { } text && expected.Literal is { } term && (predicate.Operator == "contains"
                ? text.Contains(term, StringComparison.Ordinal) : text.StartsWith(term, StringComparison.Ordinal));
        var comparison = Compare(actual, expected);
        return predicate.Operator switch
        {
            "eq" => comparison == 0,
            "ne" => comparison != 0,
            "gt" => actual.Type != "null" && expected.Type != "null" && comparison > 0,
            "gte" => actual.Type != "null" && expected.Type != "null" && comparison >= 0,
            "lt" => actual.Type != "null" && expected.Type != "null" && comparison < 0,
            "lte" => actual.Type != "null" && expected.Type != "null" && comparison <= 0,
            _ => false
        };
    }

    public static int Compare(DashboardValue left, DashboardValue right)
    {
        if (left.Type == "null" || right.Type == "null") return left.Type == right.Type ? 0 : left.Type == "null" ? -1 : 1;
        if (left.Type is "decimal" or "integer" && right.Type is "decimal" or "integer")
            return decimal.Parse(left.Literal!, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(right.Literal!, CultureInfo.InvariantCulture));
        if (left.Type == "instant" && right.Type == "instant")
            return DateTimeOffset.Parse(left.Literal!, CultureInfo.InvariantCulture).CompareTo(DateTimeOffset.Parse(right.Literal!, CultureInfo.InvariantCulture));
        return StringComparer.Ordinal.Compare(left.Literal, right.Literal);
    }

    public static DashboardValue String(string? value) => value is null ? new("null") : new("string", value);
    public static DashboardValue Instant(DateTimeOffset? value) => value is null ? new("null") : new("instant", value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    public static DashboardValue Decimal(decimal? value) => value is null ? new("null") : new("decimal", value.Value.ToString(CultureInfo.InvariantCulture));
}
