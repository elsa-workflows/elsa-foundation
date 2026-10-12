import type { StudioThemeLayout } from "../../sdk";

/**
 * Where the main navigation lives, independent of the theme: `left` is the docked sidebar, `top` a
 * horizontal menubar with a status bar. ThemeProvider publishes the resolved mode as `data-nav-mode`
 * on <html> (below 1025px both collapse to the same responsive strip).
 */
export type NavMode = "left" | "top";

/** What the user picked: `theme` follows the active theme's layout, the other two override it. */
export type NavModePreference = "theme" | NavMode;

export const navModePreferences: readonly NavModePreference[] = ["theme", "left", "top"] as const;

export const navModePreferenceLabels: Record<NavModePreference, string> = {
  theme: "Theme default",
  left: "Left",
  top: "Top"
};

export const navModeAttribute = "data-nav-mode";
export const navModeStorageKey = "elsa-studio-nav-mode";

export function isNavModePreference(value: unknown): value is NavModePreference {
  return typeof value === "string" && (navModePreferences as readonly string[]).includes(value);
}

/** The mode a theme asks for when the user has not chosen one: the workbench layout is the top menubar. */
export function getThemeNavMode(layout: StudioThemeLayout | undefined): NavMode {
  return layout === "workbench" ? "top" : "left";
}

export function resolveNavMode(layout: StudioThemeLayout | undefined, preference: NavModePreference): NavMode {
  return preference === "theme" ? getThemeNavMode(layout) : preference;
}
