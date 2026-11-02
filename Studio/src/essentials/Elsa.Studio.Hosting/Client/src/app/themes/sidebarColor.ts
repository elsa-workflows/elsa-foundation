import { getThemeModeDefinition, type Theme, type ThemeColors, type ThemeMode } from "./presets";
import type { WorkspaceStyle } from "./workspaceStyles";

/**
 * The tone of the main navigation surface, independent of the workspace appearance. `dark` and
 * `light` pin the navigation to that palette mode; `theme` follows the workspace style and mode.
 * ThemeProvider publishes a pinned tone as `data-sidebar-color` on <html>; High contrast keeps its
 * own navigation and never carries the attribute.
 */
export type SidebarColor = "light" | "dark";

export type SidebarColorPreference = "theme" | SidebarColor;

export const sidebarColorPreferences: readonly SidebarColorPreference[] = ["theme", "light", "dark"] as const;

export const sidebarColorPreferenceLabels: Record<SidebarColorPreference, string> = {
  theme: "Theme default",
  light: "Light",
  dark: "Dark"
};

export const sidebarColorAttribute = "data-sidebar-color";
export const sidebarColorStorageKey = "elsa-studio-sidebar-color";

export function isSidebarColorPreference(value: unknown): value is SidebarColorPreference {
  return typeof value === "string" && (sidebarColorPreferences as readonly string[]).includes(value);
}

/** The `--studio-nav-*` roles the navigation surface is painted with. */
export interface NavigationColors {
  bg: string;
  text: string;
  active: string;
  activeText: string;
}

/** The tone pinned on the navigation, or undefined when it follows the workspace. */
export function resolveSidebarColor(preference: SidebarColorPreference, mode: ThemeMode): SidebarColor | undefined {
  return preference === "theme" || mode === "high-contrast" ? undefined : preference;
}

/**
 * The navigation colors for the active palette and mode. A pinned tone takes the palette's
 * navigation pair from that mode; when that pair is not itself in the tone (some light modes
 * ship a dark sidebar), the mode's card surface is used instead so Light really is light.
 */
export function resolveNavigationColors(
  colorScheme: Pick<Theme, "modes">,
  mode: ThemeMode,
  workspaceStyle: WorkspaceStyle | null,
  preference: SidebarColorPreference
): NavigationColors {
  const colors = getThemeModeDefinition(colorScheme, mode) ?? colorScheme.modes.dark;
  const tone = resolveSidebarColor(preference, mode);
  if (tone === undefined) {
    // In light Linen use the palette's dark navigation pair; light-only palettes keep their own pair.
    const source = workspaceStyle === "linen" && mode === "light" ? getThemeModeDefinition(colorScheme, "dark") ?? colors : colors;
    return sidebarPair(source);
  }

  const source = getThemeModeDefinition(colorScheme, tone) ?? colors;
  const isDark = isDarkColor(source.sidebar);
  if (isDark === undefined || isDark === (tone === "dark")) return sidebarPair(source);
  return { bg: source.card, text: source.cardForeground, active: source.muted, activeText: source.foreground };
}

function sidebarPair(colors: ThemeColors): NavigationColors {
  return { bg: colors.sidebar, text: colors.sidebarForeground, active: colors.sidebarActive, activeText: colors.sidebarActiveForeground };
}

/** Whether a palette color reads as dark; undefined for a value it cannot parse, such as `var(...)`. */
export function isDarkColor(value: string): boolean | undefined {
  const lightness = perceivedLightness(value.trim());
  return lightness === undefined ? undefined : lightness < 0.6;
}

/** OKLab-style perceived lightness (0–1) of an oklch, hex, rgb or hsl color. */
function perceivedLightness(value: string): number | undefined {
  const oklch = value.match(/^oklch\(\s*([\d.]+)(%?)\s/i);
  if (oklch) return Number(oklch[1]) / (oklch[2] ? 100 : 1);

  const rgb = parseHex(value) ?? parseRgb(value) ?? parseHsl(value);
  if (!rgb) return undefined;
  const [r, g, b] = rgb.map(channel => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return Math.cbrt(0.2126 * r + 0.7152 * g + 0.0722 * b);
}

function parseHex(value: string): number[] | undefined {
  const match = value.match(/^#([0-9a-f]{3,8})$/i);
  if (!match) return undefined;
  const hex = match[1].length <= 4 ? [...match[1]].map(digit => digit + digit).join("") : match[1];
  return [0, 2, 4].map(offset => parseInt(hex.slice(offset, offset + 2), 16));
}

function parseRgb(value: string): number[] | undefined {
  const match = value.match(/^rgba?\(\s*([\d.]+)(%?)[\s,]+([\d.]+)(%?)[\s,]+([\d.]+)(%?)/i);
  if (!match) return undefined;
  return [1, 3, 5].map(index => Number(match[index]) * (match[index + 1] ? 2.55 : 1));
}

function parseHsl(value: string): number[] | undefined {
  const match = value.match(/^hsla?\(\s*([\d.]+)(?:deg)?[\s,]+([\d.]+)%[\s,]+([\d.]+)%/i);
  if (!match) return undefined;
  const [h, s, l] = [Number(match[1]) % 360, Number(match[2]) / 100, Number(match[3]) / 100];
  const chroma = (1 - Math.abs(2 * l - 1)) * s;
  const channel = (n: number) => {
    const k = (n + h / 30) % 12;
    return (l - chroma / 2 * Math.max(-1, Math.min(k - 3, 9 - k, 1))) * 255;
  };
  return [channel(0), channel(8), channel(4)];
}
