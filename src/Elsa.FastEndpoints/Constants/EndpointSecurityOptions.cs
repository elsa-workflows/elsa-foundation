
namespace Elsa.FastEndpoints.Constants
{
    public static class EndpointSecurityOptions
    {
        public const string AdminRoleName = "Admin";
        public const string ReaderRoleName = "Reader";
        public const string WriteRoleName = "Writer";
        public static bool SecurityIsEnabled { get; private set; } = true;

        public static void DisableSecurity() => SecurityIsEnabled = false;
    }
}
