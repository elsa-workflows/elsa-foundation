using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;

namespace Elsa.Studio.Preferences.Api.Services;

public static class StudioPreferencePreconditions
{
    public static StudioPreferenceWriteCondition Parse(string? ifMatch, string? ifNoneMatch)
    {
        if (string.Equals(ifNoneMatch, "*", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(ifMatch))
            return StudioPreferenceWriteCondition.MustNotExist;
        if (string.IsNullOrWhiteSpace(ifNoneMatch) && TryUnquote(ifMatch, out var revision))
            return StudioPreferenceWriteCondition.Matches(revision);

        throw new StudioPreferenceValidationException("Use If-None-Match: * to create or one quoted If-Match revision to update.");
    }

    private static bool TryUnquote(string? value, out string revision)
    {
        revision = "";
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || value[0] != '"' || value[^1] != '"')
            return false;
        revision = value[1..^1];
        return !string.IsNullOrWhiteSpace(revision);
    }
}
