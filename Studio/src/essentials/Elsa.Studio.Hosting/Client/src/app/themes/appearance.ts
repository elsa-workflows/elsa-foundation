import { isThemeMode, themeModeLabels, type ThemeMode } from "./presets";
import { isNavModePreference, navModeStorageKey, type NavModePreference } from "./navMode";
import { getStudioRuntimeConfig } from "../runtime";
import { isWorkspaceStyle, type WorkspaceStyle } from "./workspaceStyles";
import { isSidebarColorPreference, sidebarColorStorageKey, type SidebarColorPreference } from "./sidebarColor";

/**
 * The user's appearance choices as one model: base preset, workspace style, color scheme,
 * mode, navigation and sidebar color. ThemeProvider owns the live state; this module owns the shape, the defaults,
 * persistence and resolution, so the topbar menu and a Settings › Appearance page share one source.
 *
 * `system` is a mode preference, not a theme mode: it follows the OS contrast and colour-scheme
 * preferences and keeps following them while they change.
 */
export type ThemeModePreference = ThemeMode | "system";

export interface AppearancePreferences {
  /** Theme id, or null for the configured default theme. */
  themeId: string | null;
  workspaceStyle: WorkspaceStyle | null;
  colorSchemeId: string | null;
  mode: ThemeModePreference;
  navigation: NavModePreference;
  sidebarColor: SidebarColorPreference;
}

export const appearanceStorageKeys = {
  themeId: "elsa-studio-theme",
  workspaceStyle: "elsa-studio-workspace-style",
  colorSchemeId: "elsa-studio-color-scheme",
  mode: "elsa-studio-theme-mode",
  navigation: navModeStorageKey,
  sidebarColor: sidebarColorStorageKey
} as const;

/** Sidebar and dock keys Reset also clears. These are chrome, not theme/mode/nav preferences. */
export const appearanceChromeStorageKeys = {
  sidebarCollapsed: "elsa-studio-sidebar-collapsed",
  bottomPanelCollapsed: "elsa-studio-bottom-panel-collapsed",
  bottomPanelHeight: "elsa-studio-bottom-panel-height",
  bottomPanelMaximized: "elsa-studio-bottom-panel-maximized",
  activeBottomPanel: "elsa-studio-active-bottom-panel"
} as const;

/** Every appearance and chrome override cleared by Reset. */
export const appearanceResetStorageKeys = [
  appearanceStorageKeys.themeId,
  appearanceStorageKeys.workspaceStyle,
  appearanceStorageKeys.colorSchemeId,
  appearanceStorageKeys.mode,
  appearanceStorageKeys.navigation,
  appearanceStorageKeys.sidebarColor,
  appearanceChromeStorageKeys.sidebarCollapsed,
  appearanceChromeStorageKeys.bottomPanelCollapsed,
  appearanceChromeStorageKeys.bottomPanelHeight,
  appearanceChromeStorageKeys.bottomPanelMaximized,
  appearanceChromeStorageKeys.activeBottomPanel
] as const;

export const appearanceResetEventName = "elsa-studio:appearance-reset";

export const themeModePreferenceLabels: Record<ThemeModePreference, string> = {
  system: "System",
  ...themeModeLabels
};

export const appearanceModePreferences: readonly ThemeModePreference[] = [
  "system",
  "light",
  "dark",
  "dim",
  "high-contrast"
];

export const defaultAppearancePreferences: AppearancePreferences = {
  themeId: null,
  workspaceStyle: "linen",
  colorSchemeId: null,
  mode: "system",
  navigation: "theme",
  // A dark navigation keeps its contrast against every workspace appearance.
  sidebarColor: "dark"
};

export function isThemeModePreference(value: unknown): value is ThemeModePreference {
  return value === "system" || isThemeMode(value);
}

interface PreferenceStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

function defaultStorage(): PreferenceStorage | undefined {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}

function safely<T>(action: () => T, fallback: T): T {
  try {
    return action();
  } catch {
    // Appearance is a best-effort browser preference; storage failures must not block Studio.
    return fallback;
  }
}

/** Host defaults are only fallbacks; an explicit user preference always wins. */
export function getDefaultAppearancePreferences(): AppearancePreferences {
  const configured = getStudioRuntimeConfig().appearance;
  return {
    ...defaultAppearancePreferences,
    workspaceStyle: configured?.workspaceStyle === "preset" ? null : isWorkspaceStyle(configured?.workspaceStyle) ? configured.workspaceStyle : "linen",
    colorSchemeId: configured?.paletteId || null,
    mode: isThemeModePreference(configured?.mode) ? configured.mode : "system",
    navigation: isNavModePreference(configured?.navigation) ? configured.navigation : "theme"
  };
}

/** Distinguishes a stored choice from a resolved host fallback during reconciliation. */
export function hasStoredAppearancePreference(key: keyof AppearancePreferences, storage: PreferenceStorage | undefined = defaultStorage()): boolean {
  return storage ? safely(() => storage.getItem(appearanceStorageKeys[key]) !== null, false) : false;
}

/** Reads stored preferences; unknown or missing values fall back to the defaults. */
export function readAppearancePreferences(storage: PreferenceStorage | undefined = defaultStorage()): AppearancePreferences {
  const read = (key: string) => (storage ? safely(() => storage.getItem(key), null) : null);
  const mode = read(appearanceStorageKeys.mode);
  const navigation = read(appearanceStorageKeys.navigation);
  const sidebarColor = read(appearanceStorageKeys.sidebarColor);
  const workspaceStyle = read(appearanceStorageKeys.workspaceStyle);
  const themeId = read(appearanceStorageKeys.themeId) || null;
  const colorSchemeId = read(appearanceStorageKeys.colorSchemeId);
  const defaults = getDefaultAppearancePreferences();
  return {
    themeId,
    workspaceStyle: workspaceStyle === "preset" || (themeId !== null && !isWorkspaceStyle(workspaceStyle)) ? null : isWorkspaceStyle(workspaceStyle) ? workspaceStyle : defaults.workspaceStyle,
    colorSchemeId: colorSchemeId === "@theme" ? null : colorSchemeId || (themeId ? null : defaults.colorSchemeId),
    mode: isThemeModePreference(mode) ? mode : defaults.mode,
    navigation: isNavModePreference(navigation) ? navigation : defaults.navigation,
    sidebarColor: isSidebarColorPreference(sidebarColor) ? sidebarColor : defaults.sidebarColor
  };
}

/** Persists the given subset of preferences. A null theme id clears the stored theme. */
export function writeAppearancePreferences(
  preferences: Partial<AppearancePreferences>,
  storage: PreferenceStorage | undefined = defaultStorage()
) {
  if (!storage) return;
  safely(() => {
    if (preferences.themeId !== undefined) {
      if (preferences.themeId === null) storage.removeItem(appearanceStorageKeys.themeId);
      else storage.setItem(appearanceStorageKeys.themeId, preferences.themeId);
    }
    for (const key of ["workspaceStyle", "colorSchemeId"] as const) {
      const value = preferences[key];
      if (value === null && key === "workspaceStyle") storage.setItem(appearanceStorageKeys[key], "preset");
      else if (value === null) storage.setItem(appearanceStorageKeys[key], "@theme");
      else if (value !== undefined) storage.setItem(appearanceStorageKeys[key], value);
    }
    if (preferences.mode !== undefined) storage.setItem(appearanceStorageKeys.mode, preferences.mode);
    if (preferences.navigation !== undefined) storage.setItem(appearanceStorageKeys.navigation, preferences.navigation);
    if (preferences.sidebarColor !== undefined) storage.setItem(appearanceStorageKeys.sidebarColor, preferences.sidebarColor);
  }, undefined);
}

/** Removes every stored appearance preference and chrome key so the defaults apply again. */
export function resetAppearancePreferences(storage: PreferenceStorage | undefined = defaultStorage()): AppearancePreferences {
  if (storage) {
    appearanceResetStorageKeys.forEach(key => safely(() => storage.removeItem(key), undefined));
  }
  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(appearanceResetEventName));
  }
  return getDefaultAppearancePreferences();
}

type MatchMedia = (query: string) => { matches: boolean };

function defaultMatchMedia(): MatchMedia | undefined {
  return typeof window !== "undefined" && typeof window.matchMedia === "function" ? q => window.matchMedia(q) : undefined;
}

/** The theme mode the OS asks for: high contrast first, then dark, else light. */
export function getSystemThemeMode(matchMedia: MatchMedia | undefined = defaultMatchMedia()): ThemeMode {
  if (!matchMedia) return "light";
  if (matchMedia("(prefers-contrast: more)").matches) return "high-contrast";
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/** Turns a mode preference into a concrete theme mode. */
export function resolveModePreference(preference: ThemeModePreference, matchMedia?: MatchMedia): ThemeMode {
  return preference === "system" ? getSystemThemeMode(matchMedia) : preference;
}

/** Calls `onChange` whenever the OS colour-scheme or contrast preference changes. Returns an unsubscribe. */
export function watchSystemThemeMode(onChange: () => void): () => void {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return () => undefined;
  const queries = ["(prefers-color-scheme: dark)", "(prefers-contrast: more)"].map(q => window.matchMedia(q));
  queries.forEach(q => q.addEventListener?.("change", onChange));
  return () => queries.forEach(q => q.removeEventListener?.("change", onChange));
}
