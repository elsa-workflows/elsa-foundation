using System.Text;
using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityTypeKeyPolicy : IActivityTypeKeyPolicy
{
    private const string Prefix = "elsa.user";
    private const int MaximumLength = 160;

    public ActivityTypeKeyRules Rules { get; } = new(
        ServerGenerated: true,
        AllowsPreCreationOverride: true,
        Immutable: true,
        Prefix,
        "^elsa\\.user\\.[a-z0-9]+(?:-[a-z0-9]+)*\\.[a-z0-9]+(?:-[a-z0-9]+)*$",
        MaximumLength,
        "tenantId + activityTypeKey");

    public string Generate(string displayName, string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("A definition identity is required.", nameof(definitionId));
        var identity = Slug(definitionId);
        var availableDisplayLength = MaximumLength - Prefix.Length - identity.Length - 2;
        if (availableDisplayLength < 1)
            throw new ArgumentException("The definition identity is too long to form an activity type key.", nameof(definitionId));
        var display = Slug(displayName);
        if (display.Length > availableDisplayLength)
            display = display[..availableDisplayLength].TrimEnd('-');
        return $"{Prefix}.{display}.{identity}";
    }

    public string NormalizeAndValidateOverride(string activityTypeKey)
    {
        if (string.IsNullOrWhiteSpace(activityTypeKey))
            throw new ArgumentException("An activity type key is required.", nameof(activityTypeKey));

        var normalized = activityTypeKey.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (normalized.Length > Rules.MaximumLength)
            throw new ArgumentException($"The activity type key must not exceed {Rules.MaximumLength} characters.", nameof(activityTypeKey));
        if (!normalized.StartsWith($"{Rules.Prefix}.", StringComparison.Ordinal))
            throw new ArgumentException($"The activity type key must use the '{Rules.Prefix}' prefix.", nameof(activityTypeKey));
        if (!Regex.IsMatch(normalized, Rules.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("The activity type key does not match the required pattern.", nameof(activityTypeKey));
        return normalized;
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var needsSeparator = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (character is >= 'A' and <= 'Z')
            {
                if (needsSeparator && builder.Length > 0)
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                needsSeparator = false;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (needsSeparator && builder.Length > 0)
                    builder.Append('-');
                builder.Append(character);
                needsSeparator = false;
            }
            else if (builder.Length > 0)
                needsSeparator = true;
        }
        return builder.Length == 0 ? "activity" : builder.ToString();
    }
}
