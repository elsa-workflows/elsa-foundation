using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Studio.Preferences.Core;

public static class StudioPreferencesPermissions
{
    public const string Read = "studio.preferences.read";
    public const string Write = "studio.preferences.write";
}

public sealed class StudioPreferencesPermissionContributor : IPermissionContributor
{
    public IEnumerable<Permission> Contribute() =>
    [
        new(StudioPreferencesPermissions.Read, "Read Studio preferences", "Studio", "Read personal Studio preference documents."),
        new(
            StudioPreferencesPermissions.Write,
            "Write Studio preferences",
            "Studio",
            "Create and update personal Studio preference documents.",
            new HashSet<string> { StudioPreferencesPermissions.Read })
    ];
}
