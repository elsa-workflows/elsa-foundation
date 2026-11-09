namespace Elsa.Integrations.Connectors.Slack;

internal static class SlackApi
{
    public const string BaseUrl = "https://slack.com/api/";
    public const string PostMessage = "chat.postMessage";
    public const string UpdateMessage = "chat.update";
    public const string AddReaction = "reactions.add";
    public const string ListConversations = "conversations.list";
    public const string ConversationInfo = "conversations.info";
    public const string LookupUserByEmail = "users.lookupByEmail";
    public const string GetUploadUrlExternal = "files.getUploadURLExternal";
    public const string CompleteUploadExternal = "files.completeUploadExternal";
}

internal static class SlackErrorCodes
{
    public const string InvalidAuth = "invalid_auth";
    public const string TokenRevoked = "token_revoked";
    public const string AccountInactive = "account_inactive";
    public const string MissingScope = "missing_scope";
    public const string UsersNotFound = "users_not_found";
    public const string RateLimited = "rate_limited";
    public const string InvalidInput = "invalid_input";
    public const string InvalidResponse = "invalid_response";
    public const string HttpError = "http_error";
}

internal static class SlackScopes
{
    public const string ChatWrite = "chat:write";
    public const string ChatWritePublic = "chat:write.public";
    public const string ReactionsWrite = "reactions:write";
    public const string ChannelsRead = "channels:read";
    public const string GroupsRead = "groups:read";
    public const string ImRead = "im:read";
    public const string MpimRead = "mpim:read";
    public const string UsersRead = "users:read";
    public const string UsersReadEmail = "users:read.email";
    public const string FilesWrite = "files:write";
}
