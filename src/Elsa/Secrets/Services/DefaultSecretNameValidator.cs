using System.Text.RegularExpressions;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed partial class DefaultSecretNameValidator : ISecretNameValidator
{
    public bool IsValid(string? name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Secret name is required.";
            return false;
        }

        var normalizedName = Normalize(name);

        if (!SecretNameRegex().IsMatch(normalizedName))
        {
            error = "Secret name may contain only letters, numbers, dot, dash, underscore, and colon.";
            return false;
        }

        error = null;
        return true;
    }

    public string Normalize(string name) => name.Trim().ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretNameRegex();
}
