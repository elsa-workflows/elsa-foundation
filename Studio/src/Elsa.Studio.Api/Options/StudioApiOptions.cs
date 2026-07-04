using System.Reflection;

namespace Elsa.Studio.Api.Options;

public sealed class StudioApiOptions
{
    /// <summary>
    /// The host version advertised to modules and used by the compatibility gate. Defaults to the
    /// Studio API assembly's informational/file version rather than a hardcoded constant, and can be
    /// overridden via the <c>Studio:Api:HostVersion</c> configuration key or the shell feature options.
    /// </summary>
    public string HostVersion { get; set; } = ResolveAssemblyVersion();

    /// <summary>
    /// The SDK version advertised to modules and used by the compatibility gate. Defaults to the Studio
    /// API assembly's informational/file version rather than a hardcoded constant, and can be overridden
    /// via the <c>Studio:Api:SdkVersion</c> configuration key or the shell feature options.
    /// </summary>
    public string SdkVersion { get; set; } = ResolveAssemblyVersion();

    public ISet<string> DisabledModuleIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The version stamped on the Studio API assembly, used as the fallback host/SDK version when
    /// nothing is configured. Prefers the informational version (which carries the full SemVer,
    /// including any pre-release suffix), then the file version, then the assembly version.
    /// </summary>
    public static string ResolveAssemblyVersion()
    {
        var assembly = typeof(StudioApiOptions).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends a '+<commit-sha>' build-metadata suffix to the informational version;
            // strip it so the value is a clean SemVer the major-compatibility check can parse.
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
            return fileVersion;

        return assembly.GetName().Version?.ToString() ?? "1.0.0";
    }
}
