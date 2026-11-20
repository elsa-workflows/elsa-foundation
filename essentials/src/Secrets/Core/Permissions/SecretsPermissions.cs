namespace Elsa.Secrets.Core.Permissions;

public static class SecretsPermissions
{
    public const string Read = "secrets:read";
    public const string Write = "secrets:write";
    public const string UpdateValue = "secrets:update-value";
    public const string Delete = "secrets:delete";
    public const string Test = "secrets:test";
    public const string Use = "secrets:use";
    public const string Import = "secrets:import";
    public const string Export = "secrets:export";
}
