using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public static class SecretsSearchKeys
{
    /// <summary>The stable identity of the persisted case-insensitive projection.</summary>
    public const string UnicodeOrdinalIgnoreCaseAlgorithmId = SecretsUnicodeOrdinalIgnoreCaseV1.AlgorithmId;

    /// <summary>The Unicode data version owned by the persisted projection.</summary>
    public const string UnicodeVersion = SecretsUnicodeOrdinalIgnoreCaseV1.UnicodeVersion;

    public static string SearchKey(string value) => SecretsUnicodeOrdinalIgnoreCaseV1.Project(value);

    public static string LookupKey(string value) => SecretsUnicodeOrdinalIgnoreCaseV1.Project(value);

    public static string StatusValue(SecretStatus status) => status.ToString().ToLowerInvariant();
}
