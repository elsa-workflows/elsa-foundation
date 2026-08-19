namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Shared <c>Switch</c> case rules so the design-time validator, the compile-time structure handler, and
/// the runtime navigator all agree on what makes two cases collide. A <c>Switch</c> selects the first case
/// whose match value equals the evaluated value using ordinal comparison; duplicate match values are
/// therefore detected with that same ordinal comparison at every layer.
/// </summary>
public static class SwitchCaseRules
{
    /// <summary>
    /// Returns each match value that appears on more than one case, in first-seen order. Empty when every
    /// case has a unique match value.
    /// </summary>
    public static IEnumerable<string> DuplicateMatches(IEnumerable<string> matchValues) =>
        matchValues
            .GroupBy(match => match, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
}
