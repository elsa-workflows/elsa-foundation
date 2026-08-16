using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Attention.Api;

public static class AttentionPermissions
{
    public const string Read = "attention.read";
}

public sealed class AttentionPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Attention.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(AttentionPermissions.Read, "Read attention items", "Attention", "Read aggregated attention items for the authenticated tenant.")
    ];
}
