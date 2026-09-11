namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

/// <summary>
/// Resolves the design-time connection for <c>dotnet ef</c>. The tools' <c>--connection</c> flag
/// overrides this when present. Otherwise an environment variable lets CI/ops point at a real
/// engine without editing factories.
/// </summary>
internal static class SecretsDesignTimeConnection
{
    public const string SqliteVariable = "ELSA_SECRETS_EF_SQLITE";
    public const string SqlServerVariable = "ELSA_SECRETS_EF_SQLSERVER";
    public const string PostgreSqlVariable = "ELSA_SECRETS_EF_POSTGRESQL";

    public static string Resolve(string environmentVariable, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }
}
