namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Reserved owner/run-as values on a connection requirement and resolver (plan D5). Only
/// <see cref="Tenant"/> is valid in v1.
/// </summary>
public static class ConnectionOwners
{
    public const string Tenant = "tenant";
}

/// <summary>
/// Reserved <c>ManagedBy</c> values (plan D4). Only <see cref="Studio"/> is valid in v1.
/// </summary>
public static class ConnectionManagedBy
{
    public const string Studio = "studio";
}
