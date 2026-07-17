using System.Text;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityTypeKeyPolicy : IActivityTypeKeyPolicy
{
    private const string Prefix = "elsa.user";
    private const int MaximumLength = 160;

    public ActivityTypeKeyRules Rules { get; } = new(
        ServerGenerated: true,
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
