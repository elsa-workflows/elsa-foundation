namespace Elsa.Admin.Api.Options;

public sealed class AdminApiOptions
{
    public string HostVersion { get; set; } = "1.0.0";

    public string SdkVersion { get; set; } = "1.0.0";

    public ISet<string> DisabledModuleIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
