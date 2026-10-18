namespace Elsa.Connections.Core.Models;

public static class ConnectionExpressionTypes
{
    public const string Connection = "Connection";
}

public static class ConnectionInputUIHints
{
    public const string ConnectionPicker = "connection-picker";
}

public static class ConnectionTypeNames
{
    public const string Http = "http";
}

public static class HttpAuthSchemes
{
    public const string ApiKey = "api-key";
    public const string Basic = "basic";
    public const string Bearer = "bearer";
}

public static class ConnectionOwners
{
    /// <summary>The only owner valid in v1 (D5). User-delegated owners are reserved.</summary>
    public const string Tenant = "tenant";
}

public static class ConnectionManagedBy
{
    /// <summary>The only managed-by value valid in v1 (D4). Config reconciliation is deferred.</summary>
    public const string Studio = "studio";
}

public static class ConnectionStatuses
{
    public const string Ready = "Ready";
    public const string NeedsAuthorization = "NeedsAuthorization";
    public const string Error = "Error";
    public const string Revoked = "Revoked";
}

public static class ConnectionCredentialSourceKinds
{
    public const string Secret = "secret";
    public const string NamedCredential = "named-credential";
}

public static class NamedCredentialOptionKeys
{
    public const string SectionName = "Elsa:Credentials";
    public const string Kind = "Kind";
    public const string Token = "Token";
    public const string TokenType = "TokenType";
}

public static class ConnectionFaultCodes
{
    public const string Unavailable = "ConnectionUnavailable";
    public const string HostNotAllowed = "ConnectionHostNotAllowed";
    public const string OwnerNotSupported = "ConnectionOwnerNotSupported";
    public const string InsecureHttp = "ConnectionInsecureHttp";
    public const string Forbidden = "ConnectionForbidden";
}

public static class HttpConnectionSettingKeys
{
    public const string BaseUrl = "baseUrl";
    public const string AllowedHosts = "allowedHosts";
    public const string ApiKeyHeaderName = "headerName";
    public const string ApiKeyPlacement = "placement";
    public const string ApiKeyQueryName = "queryName";
    public const string Username = "username";
    public const string AllowInsecureHttp = "allowInsecureHttp";
}

public static class HttpConnectionSecretFields
{
    public const string ApiKey = "apiKey";
    public const string Password = "password";
    public const string Token = "token";
}

public static class ApiKeyPlacements
{
    public const string Header = "header";
    public const string Query = "query";
}
