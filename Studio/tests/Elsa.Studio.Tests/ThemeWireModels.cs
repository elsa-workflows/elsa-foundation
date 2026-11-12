using System.Text.Json.Serialization;

namespace Elsa.Studio.Tests;

internal sealed record ThemeStoreResponse(StudioThemeDefinition[] Themes, string DefaultThemeId, object[] Assets,
    string[]? DisabledBuiltInThemeIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ThemesMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? DirectoryThemeIds = null);
internal sealed record StudioThemeDefinition(string Id, string Name, string? Description, string Source, int Version,
    bool Enabled, bool Published, StudioThemeModes Modes, StudioThemeMaterial? Material,
    string[]? SupportedModes = null, StudioThemeTypography? Typography = null, StudioThemeShape? Shape = null, string? Layout = null)
{
    public string? WorkspaceStyle { get; init; }
}
internal sealed record ThemePack(int Version, StudioThemeDefinition[] Themes, object[]? Assets);
internal sealed record StudioThemeModes(StudioThemeModeDefinition Light, StudioThemeModeDefinition Dark,
    StudioThemeModeDefinition? Dim = null, StudioThemeModeDefinition? HighContrast = null);
internal sealed record StudioThemeTypography(string? Sans, string? Mono, string? Display);
internal sealed record StudioThemeShape(string? RadiusSm, string? Radius, string? RadiusMd, string? RadiusLg, string? RadiusXl);
internal sealed record StudioThemeMaterial(Dictionary<string, string>? TextureAssets, Dictionary<string, string>? CssVariables);
internal sealed record StudioThemeModeMaterial(Dictionary<string, string>? TextureAssets, Dictionary<string, string>? CssVariables, int? TextureSize);
internal sealed record StudioThemeModeDefinition(string Primary, string PrimaryForeground, string Secondary, string SecondaryForeground,
    string Accent, string AccentForeground, string Success, string SuccessForeground, string Warning, string WarningForeground,
    string Danger, string DangerForeground, string Background, string Foreground, string Card, string CardForeground,
    string Muted, string MutedForeground, string Border, string Input, string Sidebar, string SidebarForeground,
    string SidebarActive, string SidebarActiveForeground, string Ring, string[] ChartColors, StudioThemeModeMaterial? Material);
