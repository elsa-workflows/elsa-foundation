namespace Elsa.Activities.Design.Api.Authorization;

/// <summary>Catalog keys owned by the Activities Design API.</summary>
public static class ActivityDesignPermissions
{
    public const string Read = "activities/design:read";
    public const string Manage = "activities/design:manage";
    public const string AuthorProvider = "activities/design:author";
    public const string ReadProviderPayload = "activities/design/provider-payload:read";
}
