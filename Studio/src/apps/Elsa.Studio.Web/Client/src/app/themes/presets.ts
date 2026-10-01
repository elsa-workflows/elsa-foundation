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

const oklchToVar = (lightness: number, chroma: number, hue: number): string =>
  `oklch(${lightness} ${chroma} ${hue})`;

function createThemeDefinition(
  id: string,
  name: string,
  description: string,
  light: ThemeModeDefinition,
  dark: ThemeModeDefinition,
  supportedModes: readonly ThemeMode[] = baseThemeModes
): Theme {
  const definition: StudioThemeDefinition = {
    id,
    name,
    description,
    source: "built-in",
    version: 1,
    enabled: true,
    published: true,
    supportedModes: [...supportedModes],
    modes: { light, dark }
  };

  return toTheme(definition);
}

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
 * palette for it; an explicit `supportedModes` list can narrow that further (Brass Instrument is dark-only).
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

// Black Glass — electric-blue "HUD" palette. Accent leans saturated azure (hue ~254),
// backgrounds are deep midnight navy (dark) / crisp ice-blue (light), text steel-blue.
const blackGlassDark: ThemeColors = {
  primary: oklchToVar(0.66, 0.19, 254),
  primaryForeground: oklchToVar(0.14, 0.03, 254),
  secondary: oklchToVar(0.24, 0.035, 254),
  secondaryForeground: oklchToVar(0.93, 0.015, 246),
  accent: oklchToVar(0.36, 0.08, 254),
  accentForeground: oklchToVar(0.95, 0.015, 246),

  success: oklchToVar(0.78, 0.16, 156),
  successForeground: oklchToVar(0.12, 0.02, 156),
  warning: oklchToVar(0.78, 0.14, 70),
  warningForeground: oklchToVar(0.12, 0.02, 70),
  danger: oklchToVar(0.72, 0.17, 18),
  dangerForeground: oklchToVar(0.12, 0.02, 18),

  background: oklchToVar(0.11, 0.022, 258),
  foreground: oklchToVar(0.94, 0.014, 246),
  card: oklchToVar(0.17, 0.028, 256),
  cardForeground: oklchToVar(0.94, 0.014, 246),
  muted: oklchToVar(0.23, 0.03, 256),
  mutedForeground: oklchToVar(0.74, 0.045, 248),
  border: oklchToVar(0.44, 0.09, 254),
  input: oklchToVar(0.18, 0.028, 256),

  sidebar: oklchToVar(0.09, 0.024, 258),
  sidebarForeground: oklchToVar(0.86, 0.03, 248),
  sidebarActive: oklchToVar(0.28, 0.09, 254),
  sidebarActiveForeground: oklchToVar(0.97, 0.015, 248),

  ring: oklchToVar(0.66, 0.19, 254),
  chartColors: [
    oklchToVar(0.68, 0.2, 254),
    oklchToVar(0.72, 0.17, 232),
    oklchToVar(0.66, 0.16, 210),
    oklchToVar(0.78, 0.16, 156),
    oklchToVar(0.62, 0.2, 288),
  ],
};

const blackGlassLight: ThemeColors = {
  primary: oklchToVar(0.55, 0.19, 254),
  primaryForeground: oklchToVar(0.98, 0.008, 246),
  secondary: oklchToVar(0.9, 0.03, 246),
  secondaryForeground: oklchToVar(0.22, 0.04, 254),
  accent: oklchToVar(0.88, 0.06, 246),
  accentForeground: oklchToVar(0.22, 0.04, 254),

  success: oklchToVar(0.55, 0.13, 156),
  successForeground: oklchToVar(0.98, 0.006, 156),
  warning: oklchToVar(0.68, 0.13, 70),
  warningForeground: oklchToVar(0.17, 0.012, 70),
  danger: oklchToVar(0.58, 0.18, 18),
  dangerForeground: oklchToVar(0.98, 0.006, 18),

  background: oklchToVar(0.95, 0.024, 244),
  foreground: oklchToVar(0.19, 0.032, 254),
  card: oklchToVar(0.99, 0.012, 244),
  cardForeground: oklchToVar(0.19, 0.032, 254),
  muted: oklchToVar(0.9, 0.026, 244),
  mutedForeground: oklchToVar(0.47, 0.05, 250),
  border: oklchToVar(0.8, 0.055, 246),
  input: oklchToVar(0.94, 0.02, 244),

  sidebar: oklchToVar(0.93, 0.03, 244),
  sidebarForeground: oklchToVar(0.24, 0.04, 254),
  sidebarActive: oklchToVar(0.85, 0.07, 248),
  sidebarActiveForeground: oklchToVar(0.19, 0.045, 254),

  ring: oklchToVar(0.55, 0.19, 254),
  chartColors: [
    oklchToVar(0.55, 0.19, 254),
    oklchToVar(0.58, 0.16, 232),
    oklchToVar(0.56, 0.14, 210),
    oklchToVar(0.55, 0.13, 156),
    oklchToVar(0.52, 0.19, 288),
  ],
};

type MaterialThemeColorConfig = {
  primary: string;
  primaryForeground: string;
  secondary: string;
  secondaryForeground?: string;
  accent: string;
  accentForeground?: string;
  success?: string;
  successForeground?: string;
  warning?: string;
  warningForeground?: string;
  danger?: string;
  dangerForeground?: string;
  background: string;
  foreground: string;
  card: string;
  cardForeground?: string;
  muted: string;
  mutedForeground: string;
  border: string;
  input?: string;
  sidebar: string;
  sidebarForeground: string;
  sidebarActive: string;
  sidebarActiveForeground?: string;
  ring?: string;
  chartColors?: string[];
};

const createMaterialThemeColors = (config: MaterialThemeColorConfig): ThemeColors => ({
  primary: config.primary,
  primaryForeground: config.primaryForeground,
  secondary: config.secondary,
  secondaryForeground: config.secondaryForeground ?? config.foreground,
  accent: config.accent,
  accentForeground: config.accentForeground ?? config.foreground,

  success: config.success ?? oklchToVar(0.68, 0.14, 150),
  successForeground: config.successForeground ?? oklchToVar(0.98, 0, 0),
  warning: config.warning ?? oklchToVar(0.72, 0.16, 70),
  warningForeground: config.warningForeground ?? oklchToVar(0.14, 0.01, 70),
  danger: config.danger ?? oklchToVar(0.62, 0.2, 26),
  dangerForeground: config.dangerForeground ?? oklchToVar(0.98, 0, 0),

  background: config.background,
  foreground: config.foreground,
  card: config.card,
  cardForeground: config.cardForeground ?? config.foreground,
  muted: config.muted,
  mutedForeground: config.mutedForeground,
  border: config.border,
  input: config.input ?? config.border,

  sidebar: config.sidebar,
  sidebarForeground: config.sidebarForeground,
  sidebarActive: config.sidebarActive,
  sidebarActiveForeground: config.sidebarActiveForeground ?? config.foreground,

  ring: config.ring ?? config.primary,
  chartColors: config.chartColors ?? [
    config.primary,
    config.accent,
    config.success ?? oklchToVar(0.68, 0.14, 150),
    config.warning ?? oklchToVar(0.72, 0.16, 70),
    config.danger ?? oklchToVar(0.62, 0.2, 26),
  ],
});

const stoneTheme = createMaterialThemeColors({
  primary: oklchToVar(0.78, 0.13, 230),
  primaryForeground: oklchToVar(0.12, 0.02, 230),
  secondary: oklchToVar(0.27, 0.014, 225),
  accent: oklchToVar(0.62, 0.08, 220),
  success: oklchToVar(0.76, 0.13, 148),
  warning: oklchToVar(0.74, 0.13, 72),
  danger: oklchToVar(0.68, 0.16, 24),
  background: oklchToVar(0.16, 0.01, 230),
  foreground: oklchToVar(0.9, 0.012, 220),
  card: oklchToVar(0.22, 0.012, 230),
  muted: oklchToVar(0.29, 0.01, 230),
  mutedForeground: oklchToVar(0.7, 0.018, 220),
  border: oklchToVar(0.38, 0.02, 225),
  sidebar: oklchToVar(0.14, 0.01, 230),
  sidebarForeground: oklchToVar(0.84, 0.012, 220),
  sidebarActive: oklchToVar(0.28, 0.025, 225),
  chartColors: [
    oklchToVar(0.78, 0.13, 230),
    oklchToVar(0.65, 0.07, 210),
    oklchToVar(0.76, 0.13, 148),
    oklchToVar(0.74, 0.13, 72),
    oklchToVar(0.68, 0.16, 24),
  ],
});

// Warm-paper retune: classic medium blue accent over a warm ivory neutral ladder
// (hue ~88 = warm paper, not cool slate). Mirrors the stone-light token block in tokens.css.
const stoneLightTheme = createMaterialThemeColors({
  primary: oklchToVar(0.51, 0.135, 257),
  primaryForeground: oklchToVar(0.98, 0.005, 90),
  secondary: oklchToVar(0.94, 0.007, 88),
  secondaryForeground: oklchToVar(0.28, 0.008, 82),
  accent: oklchToVar(0.94, 0.018, 88),
  accentForeground: oklchToVar(0.28, 0.008, 82),
  success: oklchToVar(0.55, 0.12, 148),
  warning: oklchToVar(0.68, 0.12, 72),
  danger: oklchToVar(0.58, 0.16, 24),
  background: oklchToVar(0.915, 0.008, 86),
  foreground: oklchToVar(0.28, 0.008, 82),
  card: oklchToVar(0.965, 0.006, 90),
  muted: oklchToVar(0.94, 0.007, 88),
  mutedForeground: oklchToVar(0.51, 0.012, 84),
  border: oklchToVar(0.86, 0.012, 88),
  sidebar: oklchToVar(0.925, 0.008, 86),
  sidebarForeground: oklchToVar(0.3, 0.008, 82),
  sidebarActive: oklchToVar(0.965, 0.01, 88),
  chartColors: [
    oklchToVar(0.51, 0.135, 257),
    oklchToVar(0.62, 0.05, 250),
    oklchToVar(0.55, 0.12, 148),
    oklchToVar(0.68, 0.12, 72),
    oklchToVar(0.58, 0.16, 24),
  ],
});

const blueprintTheme = createMaterialThemeColors({
  primary: oklchToVar(0.78, 0.12, 230),
  primaryForeground: oklchToVar(0.15, 0.04, 245),
  secondary: oklchToVar(0.28, 0.06, 245),
  accent: oklchToVar(0.5, 0.1, 230),
  success: oklchToVar(0.78, 0.13, 150),
  warning: oklchToVar(0.78, 0.13, 72),
  danger: oklchToVar(0.7, 0.17, 28),
  background: oklchToVar(0.22, 0.06, 245),
  foreground: oklchToVar(0.93, 0.018, 230),
  card: oklchToVar(0.27, 0.06, 245),
  muted: oklchToVar(0.34, 0.055, 245),
  mutedForeground: oklchToVar(0.75, 0.04, 230),
  border: oklchToVar(0.63, 0.065, 230),
  sidebar: oklchToVar(0.18, 0.05, 245),
  sidebarForeground: oklchToVar(0.88, 0.02, 230),
  sidebarActive: oklchToVar(0.32, 0.07, 240),
});

const blueprintLightTheme = createMaterialThemeColors({
  primary: oklchToVar(0.48, 0.13, 232),
  primaryForeground: oklchToVar(0.98, 0.006, 230),
  secondary: oklchToVar(0.88, 0.025, 230),
  secondaryForeground: oklchToVar(0.2, 0.04, 245),
  accent: oklchToVar(0.86, 0.035, 230),
  accentForeground: oklchToVar(0.2, 0.04, 245),
  success: oklchToVar(0.54, 0.12, 150),
  warning: oklchToVar(0.68, 0.13, 72),
  danger: oklchToVar(0.58, 0.16, 28),
  background: oklchToVar(0.95, 0.012, 230),
  foreground: oklchToVar(0.2, 0.04, 245),
  card: oklchToVar(0.98, 0.008, 230),
  muted: oklchToVar(0.9, 0.018, 230),
  mutedForeground: oklchToVar(0.45, 0.05, 235),
  border: oklchToVar(0.72, 0.04, 230),
  sidebar: oklchToVar(0.91, 0.018, 230),
  sidebarForeground: oklchToVar(0.23, 0.04, 245),
  sidebarActive: oklchToVar(0.84, 0.04, 230),
});

const brassTheme = createMaterialThemeColors({
  primary: oklchToVar(0.72, 0.13, 75),
  primaryForeground: oklchToVar(0.13, 0.015, 80),
  secondary: oklchToVar(0.24, 0.025, 82),
  accent: oklchToVar(0.35, 0.06, 78),
  success: oklchToVar(0.76, 0.12, 126),
  warning: oklchToVar(0.8, 0.14, 78),
  danger: oklchToVar(0.68, 0.16, 28),
  background: oklchToVar(0.15, 0.014, 82),
  foreground: oklchToVar(0.9, 0.04, 82),
  card: oklchToVar(0.19, 0.018, 82),
  muted: oklchToVar(0.27, 0.022, 82),
  mutedForeground: oklchToVar(0.78, 0.04, 82),
  border: oklchToVar(0.58, 0.075, 78),
  sidebar: oklchToVar(0.13, 0.014, 82),
  sidebarForeground: oklchToVar(0.88, 0.04, 82),
  sidebarActive: oklchToVar(0.26, 0.045, 78),
});

const brassLightTheme = createMaterialThemeColors({
  primary: oklchToVar(0.44, 0.08, 78),
  primaryForeground: oklchToVar(0.98, 0.018, 82),
  secondary: oklchToVar(0.78, 0.06, 82),
  secondaryForeground: oklchToVar(0.21, 0.026, 82),
  accent: oklchToVar(0.84, 0.08, 78),
  accentForeground: oklchToVar(0.21, 0.026, 82),
  success: oklchToVar(0.5, 0.1, 126),
  warning: oklchToVar(0.67, 0.13, 78),
  danger: oklchToVar(0.56, 0.15, 28),
  background: oklchToVar(0.78, 0.052, 82),
  foreground: oklchToVar(0.2, 0.024, 82),
  card: oklchToVar(0.86, 0.046, 82),
  muted: oklchToVar(0.72, 0.048, 82),
  mutedForeground: oklchToVar(0.38, 0.04, 82),
  border: oklchToVar(0.58, 0.062, 78),
  sidebar: oklchToVar(0.72, 0.048, 82),
  sidebarForeground: oklchToVar(0.24, 0.03, 82),
  sidebarActive: oklchToVar(0.66, 0.07, 78),
});

export const materialThemeIds = ["stone", "blueprint", "brass-instrument"] as const;

export const isMaterialTheme = (themeId: string): boolean =>
  (materialThemeIds as readonly string[]).includes(themeId);

const materialMode = (
  finish: string,
  textureAsset: string,
  depth: number,
  textureSize: number
): ThemeMaterialMode => ({
  textureAssets: { surface: textureAsset },
  textureSize,
  cssVariables: {
    "--studio-material-finish": finish,
    "--studio-material-depth": String(depth)
  }
});

// The signature themes lead the list: the first entry is the out-of-box default.
export const builtInThemeDefinitions: Theme[] = [
  ...foundationThemeDefinitions.map(toTheme),
  createThemeDefinition(
    "black-glass",
    "Black Glass",
    "Smoked-glass Studio surfaces with cyan blueprint glow.",
    { ...blackGlassLight, material: materialMode("frosted", "/studio/assets/frosted-glass-tile.png", 0.36, 420) },
    { ...blackGlassDark, material: materialMode("glass", "/studio/assets/black-glass-tile.png", 0.72, 420) }
  ),
  createThemeDefinition(
    "stone",
    "Stone",
    "Carved slate surfaces with etched workflow lines.",
    { ...stoneLightTheme, material: materialMode("mist-stone", "/studio/assets/stone-mist-tile.png", 0.58, 390) },
    { ...stoneTheme, material: materialMode("slate", "/studio/assets/stone-slate-tile.png", 0.78, 390) }
  ),
  createThemeDefinition(
    "blueprint",
    "Blueprint",
    "Architectural drafting paper with crisp cyan workflow marks.",
    { ...blueprintLightTheme, material: materialMode("drafting-paper", "/studio/assets/blueprint-drafting-tile.png", 0.16, 720) },
    { ...blueprintTheme, material: materialMode("blueprint-paper", "/studio/assets/blueprint-paper-tile.png", 0.7, 420) }
  ),
  createThemeDefinition(
    "brass-instrument",
    "Brass Instrument",
    "Dark enamel panels with machined brass controls.",
    { ...brassLightTheme, material: materialMode("champagne-brass", "/studio/assets/brass-champagne-tile.png", 0.42, 380) },
    { ...brassTheme, material: materialMode("brass-enamel", "/studio/assets/brass-enamel-tile.png", 0.78, 380) },
    ["dark"]
  ),
];

export const themes: Theme[] = builtInThemeDefinitions;

export const getTheme = (themeId: string): Theme | undefined =>
  themes.find(t => t.id === themeId);

export const getThemeNames = (): { id: string; name: string }[] =>
  themes.map(t => ({ id: t.id, name: t.name }));
