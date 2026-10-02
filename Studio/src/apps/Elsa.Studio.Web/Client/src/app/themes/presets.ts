import type { StudioThemeLayout } from "../../sdk";
import { foundationThemeDefinitions } from "./foundationThemes";

export type StudioThemeSource = "built-in" | "custom";

/**
 * The appearance a user picks. `light` and `dark` are the two base modes every theme defines;
 * `dim` (a softer mid-dark for long sessions) and `high-contrast` (black, white and one bright
 * accent, for low vision and bright rooms) are optional per theme.
 */
export type ThemeMode = "light" | "dark" | "dim" | "high-contrast";

/**
 * The luminance family of a mode. This — not the mode — is what `data-theme-mode` carries on
 * <html>, so module CSS keyed on `[data-theme-mode="dark"]` keeps working under Dim and High
 * contrast. The exact mode is exposed separately as `data-theme-appearance`.
 */
export type ThemeColorScheme = "light" | "dark";

export const allThemeModes: readonly ThemeMode[] = ["light", "dark", "dim", "high-contrast"] as const;
export const baseThemeModes: readonly ThemeMode[] = ["light", "dark"] as const;

export const themeModeLabels: Record<ThemeMode, string> = {
  light: "Light",
  dark: "Dark",
  dim: "Dim",
  "high-contrast": "High contrast"
};

/** The key a mode's palette lives under in `StudioThemeDefinition.modes` (JSON-friendly camelCase). */
export type ThemeModeKey = keyof StudioThemeModes;

export const themeModeKeys: Record<ThemeMode, ThemeModeKey> = {
  light: "light",
  dark: "dark",
  dim: "dim",
  "high-contrast": "highContrast"
};

export interface ThemeColors {
  primary: string;
  primaryForeground: string;
  secondary: string;
  secondaryForeground: string;
  accent: string;
  accentForeground: string;
  success: string;
  successForeground: string;
  warning: string;
  warningForeground: string;
  danger: string;
  dangerForeground: string;
  background: string;
  foreground: string;
  card: string;
  cardForeground: string;
  muted: string;
  mutedForeground: string;
  border: string;
  input: string;
  sidebar: string;
  sidebarForeground: string;
  sidebarActive: string;
  sidebarActiveForeground: string;
  ring: string;
  chartColors: string[];
}

export interface ThemeMaterialMode {
  textureAssets?: Record<string, string>;
  cssVariables?: Record<string, string>;
  textureSize?: number;
}

export type ThemeModeDefinition = ThemeColors & {
  material?: ThemeMaterialMode;
};

export interface StudioThemeModes {
  light: ThemeModeDefinition;
  dark: ThemeModeDefinition;
  dim?: ThemeModeDefinition;
  highContrast?: ThemeModeDefinition;
}

/** Font stacks a theme renders in. Faces must be bundled (see fonts.css) or installed locally. */
export interface ThemeTypography {
  /** UI text. Drives `--font-sans`. */
  sans?: string;
  /** Code, identifiers and expressions. Drives `--font-mono`. */
  mono?: string;
  /** Page and section titles. Drives `--font-display`; falls back to `sans`. */
  display?: string;
}

/** Corner radii, as CSS lengths. Each drives the matching `--radius*` primitive. */
export interface ThemeShape {
  radiusSm?: string;
  radius?: string;
  radiusMd?: string;
  radiusLg?: string;
  radiusXl?: string;
}

export interface StudioThemeDefinition {
  id: string;
  name: string;
  description?: string;
  source: StudioThemeSource;
  version: number;
  enabled: boolean;
  published: boolean;
  supportedModes?: ThemeMode[];
  modes: StudioThemeModes;
  typography?: ThemeTypography;
  shape?: ThemeShape;
  /** Structural arrangement of the shell and authoring surfaces; `classic` when absent. */
  layout?: StudioThemeLayout;
  material?: {
    textureAssets?: Record<string, string>;
    cssVariables?: Record<string, string>;
  };
}

export type Theme = StudioThemeDefinition & {
  light: ThemeModeDefinition;
  dark: ThemeModeDefinition;
};

export const themeTokenNames = [
  "primary",
  "primaryForeground",
  "secondary",
  "secondaryForeground",
  "accent",
  "accentForeground",
  "success",
  "successForeground",
  "warning",
  "warningForeground",
  "danger",
  "dangerForeground",
  "background",
  "foreground",
  "card",
  "cardForeground",
  "muted",
  "mutedForeground",
  "border",
  "input",
  "sidebar",
  "sidebarForeground",
  "sidebarActive",
  "sidebarActiveForeground",
  "ring"
] as const satisfies readonly (keyof Omit<ThemeColors, "chartColors">)[];

export type ThemeTokenName = (typeof themeTokenNames)[number];

export function toTheme(definition: StudioThemeDefinition): Theme {
  return {
    ...definition,
    description: definition.description ?? "",
    enabled: definition.enabled ?? true,
    published: definition.published ?? true,
    supportedModes: getSupportedThemeModes(definition),
    light: definition.modes.light,
    dark: definition.modes.dark
  };
}

export function isThemeMode(value: unknown): value is ThemeMode {
  return typeof value === "string" && (allThemeModes as readonly string[]).includes(value);
}

export function getThemeColorScheme(mode: ThemeMode): ThemeColorScheme {
  return mode === "light" ? "light" : "dark";
}

export function getThemeModeDefinition(
  theme: Pick<StudioThemeDefinition, "modes">,
  mode: ThemeMode
): ThemeModeDefinition | undefined {
  return theme.modes?.[themeModeKeys[mode]];
}

/**
 * The modes a theme offers, in canonical order. A mode is offered only when the theme defines a
 * palette for it; an explicit `supportedModes` list can narrow that further (e.g. a dark-only theme).
 */
export function getSupportedThemeModes(theme: Partial<Pick<StudioThemeDefinition, "supportedModes" | "modes">>): ThemeMode[] {
  const defined = allThemeModes.filter(mode => !theme.modes || getThemeModeDefinition(theme as Pick<StudioThemeDefinition, "modes">, mode));
  const declared = Array.isArray(theme.supportedModes) ? theme.supportedModes.filter(isThemeMode) : defined;
  const supported = defined.filter(mode => declared.includes(mode));
  return supported.length > 0 ? supported : defined.filter(mode => baseThemeModes.includes(mode));
}

export function supportsThemeMode(theme: Partial<Pick<StudioThemeDefinition, "supportedModes" | "modes">>, mode: ThemeMode): boolean {
  return getSupportedThemeModes(theme).includes(mode);
}

/**
 * Picks the mode to render a theme in. When the theme lacks the preferred mode, stay in the same
 * luminance family first — a user who prefers Dim or High contrast gets Dark, not a flash to Light.
 */
export function resolveThemeMode(theme: Partial<Pick<StudioThemeDefinition, "supportedModes" | "modes">>, preferredMode: ThemeMode): ThemeMode {
  const supported = getSupportedThemeModes(theme);
  if (supported.includes(preferredMode)) return preferredMode;
  const scheme = getThemeColorScheme(preferredMode);
  return supported.find(mode => getThemeColorScheme(mode) === scheme) ?? supported[0] ?? "light";
}

export function cloneThemeDefinition(theme: StudioThemeDefinition): StudioThemeDefinition {
  return JSON.parse(JSON.stringify({
    id: theme.id,
    name: theme.name,
    description: theme.description ?? "",
    source: theme.source,
    version: theme.version,
    enabled: theme.enabled,
    published: theme.published,
    supportedModes: getSupportedThemeModes(theme),
    modes: theme.modes,
    typography: theme.typography,
    shape: theme.shape,
    layout: theme.layout,
    material: theme.material
  })) as StudioThemeDefinition;
}

/**
 * Built-in themes that opt into the textured `--studio-material-*` treatment (ThemeProvider sets
 * `data-theme-material` for them). None ships today; the registry stays so the material
 * vocabulary keeps a single switch.
 */
export const materialThemeIds: readonly string[] = [];

export const isMaterialTheme = (themeId: string): boolean => materialThemeIds.includes(themeId);

// The signature themes are the built-in list: the first entry is the out-of-box default.
export const builtInThemeDefinitions: Theme[] = foundationThemeDefinitions.map(toTheme);

export const themes: Theme[] = builtInThemeDefinitions;

export const getTheme = (themeId: string): Theme | undefined =>
  themes.find(t => t.id === themeId);

export const getThemeNames = (): { id: string; name: string }[] =>
  themes.map(t => ({ id: t.id, name: t.name }));
