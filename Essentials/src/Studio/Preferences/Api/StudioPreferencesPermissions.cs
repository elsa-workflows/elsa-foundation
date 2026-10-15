using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Studio.Preferences.Core;

public static class StudioPreferencesPermissions
{
    public const string OwnerId = "elsa.studio.preferences";

    public const string Read = "studio/preferences:read";
    public const string Write = "studio/preferences:write";
    public const string ManageDefaults = "studio/preferences/defaults:manage";
}

public sealed class StudioPreferencesPermissionContributor : IPermissionContributor
{
    public string OwnerId => StudioPreferencesPermissions.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(StudioPreferencesPermissions.Read, "Read Studio preferences", "Studio", "Read personal Studio preference documents."),
        new(
            StudioPreferencesPermissions.Write,
            "Write Studio preferences",
            "Studio",
            "Create and update personal Studio preference documents.",
            new HashSet<string> { StudioPreferencesPermissions.Read }),
        new(
            StudioPreferencesPermissions.ManageDefaults,
            "Manage Studio preference defaults",
            "Studio",
            "Read and write the admin default Home layout for a tenant and host.")
    ];
}
