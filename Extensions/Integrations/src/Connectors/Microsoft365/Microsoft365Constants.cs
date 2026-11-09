namespace Elsa.Integrations.Connectors.Microsoft365;

internal static class Microsoft365Api
{
    public const string DefaultBaseUrl = "https://graph.microsoft.com";
    public const string VersionSegment = "v1.0";
    public const int SimpleUploadMaxBytes = 4 * 1024 * 1024;
    public const int UploadChunkMultipleBytes = 320 * 1024;
    public const int DefaultUploadChunkSizeBytes = 10 * UploadChunkMultipleBytes;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 999;
    public const int DefaultMaxItems = 100;
    public const int AbsoluteMaxItems = 1000;
    public const int ErrorTextLimit = 256;
}

internal static class Microsoft365ErrorCodes
{
    public const string InvalidAuthenticationToken = "InvalidAuthenticationToken";
    public const string InvalidGrant = "invalid_grant";
    public const string AuthorizationRequestDenied = "Authorization_RequestDenied";
    public const string ErrorAccessDenied = "ErrorAccessDenied";
    public const string RateLimited = "rate_limited";
    public const string InvalidInput = "invalid_input";
    public const string InvalidResponse = "invalid_response";
    public const string HttpError = "http_error";
}

internal static class Microsoft365Scopes
{
    public const string MailSend = "Mail.Send";
    public const string MailRead = "Mail.Read";
    public const string CalendarsReadWrite = "Calendars.ReadWrite";
    public const string CalendarsRead = "Calendars.Read";
    public const string UserReadBasicAll = "User.ReadBasic.All";
    public const string FilesReadWrite = "Files.ReadWrite";
    public const string FilesRead = "Files.Read";
    public const string SitesReadWriteAll = "Sites.ReadWrite.All";
    public const string ChannelMessageSend = "ChannelMessage.Send";
    public const string OfflineAccess = "offline_access";
}
