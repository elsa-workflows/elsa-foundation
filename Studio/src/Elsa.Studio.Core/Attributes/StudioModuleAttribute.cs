namespace Elsa.Studio.Core.Attributes;

/// <summary>
/// Marks a <c>[ShellFeature]</c> class as a Studio UI module. The module manifest is built automatically
/// from the attribute data — no handler class or extension method required.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StudioModuleAttribute : Attribute
{
    public StudioModuleAttribute(string slug, string displayName, string version, params string[] capabilities)
    {
        Slug = slug;
        DisplayName = displayName;
        Version = version;
        Capabilities = capabilities;
    }

    /// <summary>Path segment under <c>studio/modules/</c> (e.g. "workflows", "expression-editors/liquid").</summary>
    public string Slug { get; }

    /// <summary>Short human-readable name shown in the module registry.</summary>
    public string DisplayName { get; }

    /// <summary>Module version used for cache-busting entry/style URLs.</summary>
    public string Version { get; }

    /// <summary>Capabilities this module contributes (e.g. "navigation", "routes", "http").</summary>
    public string[] Capabilities { get; }

    /// <summary>Whether the module has a companion CSS file. Defaults to <c>true</c>.</summary>
    public bool HasStyles { get; set; } = true;

    /// <summary>Minimum host version required. Defaults to <c>"^1.0.0"</c>.</summary>
    public string RequiredHostVersion { get; set; } = "^1.0.0";

    /// <summary>Minimum SDK version required. Defaults to <c>"^1.0.0"</c>.</summary>
    public string RequiredSdkVersion { get; set; } = "^1.0.0";
}
