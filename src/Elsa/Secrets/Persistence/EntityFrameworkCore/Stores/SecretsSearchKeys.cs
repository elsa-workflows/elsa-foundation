using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public static class SecretsSearchKeys
{
    public static string SearchKey(string value) => value.ToUpperInvariant();

    public static string LookupKey(string value) => value.ToUpperInvariant();

    public static string StatusValue(SecretStatus status) => status.ToString().ToLowerInvariant();
}
