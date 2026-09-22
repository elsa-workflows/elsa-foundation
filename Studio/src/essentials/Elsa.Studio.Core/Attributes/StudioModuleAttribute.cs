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

    /// <summary>
    /// Host version range this module requires, e.g. <c>"^4.0.0"</c>. Defaults to <c>"*"</c> — no constraint.
    /// <para>
    /// The default is deliberately unconstrained. The host advertises its own assembly version, which CI stamps
    /// (<c>packages.yml</c> packs with <c>/p:Version</c>), so a hardcoded default range asserts a requirement the
    /// module author never chose and that goes stale the moment the version line moves. It previously defaulted to
    /// <c>"^1.0.0"</c>, which made every module fail the loader's compatibility gate in any host built from the
    /// published packages. Declare a range here only when the module genuinely needs one.
    /// </para>
    /// </summary>
    public string RequiredHostVersion { get; set; } = "*";

    /// <summary>
    /// SDK version range this module requires, e.g. <c>"^4.0.0"</c>. Defaults to <c>"*"</c> — no constraint.
    /// See <see cref="RequiredHostVersion"/> for why the default asserts nothing.
    /// </summary>
    public string RequiredSdkVersion { get; set; } = "*";
}
