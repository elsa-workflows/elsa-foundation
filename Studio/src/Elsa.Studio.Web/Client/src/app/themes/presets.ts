export type StudioThemeSource = "built-in" | "custom";
export type ThemeMode = "light" | "dark";
export const allThemeModes: readonly ThemeMode[] = ["light", "dark"] as const;

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

export interface StudioThemeDefinition {
  id: string;
  name: string;
  description?: string;
  source: StudioThemeSource;
  version: number;
  enabled: boolean;
  published: boolean;
  supportedModes?: ThemeMode[];
  modes: {
    light: ThemeModeDefinition;
    dark: ThemeModeDefinition;
  };
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

const white = oklchToVar(0.985, 0, 0);
const ink = oklchToVar(0.205, 0, 0);

function createMode(
  primary: string,
  accentHue: number,
  overrides: Partial<ThemeModeDefinition> = {}
): ThemeModeDefinition {
  const isDark = overrides.background ? getOklchLightness(overrides.background) < 0.5 : false;
  const foreground = isDark ? oklchToVar(0.96, 0.01, accentHue) : ink;
  const background = isDark ? oklchToVar(0.17, 0.02, accentHue) : oklchToVar(0.99, 0.004, accentHue);

  return {
    primary,
    primaryForeground: white,
    secondary: isDark ? oklchToVar(0.25, 0.02, accentHue) : oklchToVar(0.96, 0.006, accentHue),
    secondaryForeground: foreground,
    accent: isDark ? oklchToVar(0.32, 0.04, accentHue) : oklchToVar(0.94, 0.025, accentHue),
    accentForeground: foreground,
    success: isDark ? oklchToVar(0.65, 0.16, 150) : oklchToVar(0.75, 0.16, 150),
    successForeground: isDark ? oklchToVar(0.14, 0, 0) : white,
    warning: isDark ? oklchToVar(0.66, 0.17, 70) : oklchToVar(0.72, 0.18, 60),
    warningForeground: isDark ? oklchToVar(0.14, 0, 0) : white,
    danger: isDark ? oklchToVar(0.52, 0.22, 27) : oklchToVar(0.58, 0.245, 27),
    dangerForeground: white,
    background,
    foreground,
    card: isDark ? oklchToVar(0.23, 0.02, accentHue) : oklchToVar(1, 0, 0),
    cardForeground: foreground,
    muted: isDark ? oklchToVar(0.28, 0.015, accentHue) : oklchToVar(0.95, 0.006, accentHue),
    mutedForeground: isDark ? oklchToVar(0.72, 0.01, accentHue) : oklchToVar(0.45, 0.012, accentHue),
    border: isDark ? oklchToVar(0.35, 0.015, accentHue) : oklchToVar(0.88, 0.006, accentHue),
    input: isDark ? oklchToVar(0.29, 0.015, accentHue) : oklchToVar(0.98, 0.003, accentHue),
    sidebar: isDark ? oklchToVar(0.14, 0.02, accentHue) : oklchToVar(0.985, 0.004, accentHue),
    sidebarForeground: foreground,
    sidebarActive: isDark ? oklchToVar(0.3, 0.04, accentHue) : oklchToVar(0.93, 0.03, accentHue),
    sidebarActiveForeground: foreground,
    ring: primary,
    chartColors: [
      primary,
      oklchToVar(isDark ? 0.56 : 0.64, 0.18, 264),
      oklchToVar(isDark ? 0.64 : 0.74, 0.12, 175),
      oklchToVar(isDark ? 0.62 : 0.72, 0.18, 60),
      oklchToVar(isDark ? 0.58 : 0.68, 0.2, 320)
    ],
    ...overrides
  };
}

function createThemeDefinition(
  id: string,
  name: string,
  description: string,
  light: ThemeModeDefinition,
  dark: ThemeModeDefinition,
  supportedModes: readonly ThemeMode[] = allThemeModes
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

export function getSupportedThemeModes(theme: Pick<StudioThemeDefinition, "supportedModes">): ThemeMode[] {
  const supportedModes = Array.isArray(theme.supportedModes)
    ? theme.supportedModes.filter(isThemeMode)
    : [...allThemeModes];
  const uniqueModes = Array.from(new Set(supportedModes));
  return uniqueModes.length > 0 ? uniqueModes : [...allThemeModes];
}

export function supportsThemeMode(theme: Pick<StudioThemeDefinition, "supportedModes">, mode: ThemeMode): boolean {
  return getSupportedThemeModes(theme).includes(mode);
}

export function resolveThemeMode(theme: Pick<StudioThemeDefinition, "supportedModes">, preferredMode: ThemeMode): ThemeMode {
  return supportsThemeMode(theme, preferredMode) ? preferredMode : getSupportedThemeModes(theme)[0] ?? "light";
}

function isThemeMode(value: string): value is ThemeMode {
  return value === "light" || value === "dark";
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
  warning?: string;
  danger?: string;
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
  successForeground: oklchToVar(0.98, 0, 0),
  warning: config.warning ?? oklchToVar(0.72, 0.16, 70),
  warningForeground: oklchToVar(0.14, 0.01, 70),
  danger: config.danger ?? oklchToVar(0.62, 0.2, 26),
  dangerForeground: oklchToVar(0.98, 0, 0),

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

const paperTheme = createMaterialThemeColors({
  primary: oklchToVar(0.43, 0.13, 258),
  primaryForeground: oklchToVar(0.98, 0.012, 85),
  secondary: oklchToVar(0.88, 0.032, 82),
  secondaryForeground: oklchToVar(0.25, 0.018, 75),
  accent: oklchToVar(0.88, 0.04, 76),
  accentForeground: oklchToVar(0.25, 0.018, 75),
  success: oklchToVar(0.53, 0.12, 148),
  warning: oklchToVar(0.72, 0.13, 70),
  danger: oklchToVar(0.54, 0.16, 28),
  background: oklchToVar(0.92, 0.027, 82),
  foreground: oklchToVar(0.23, 0.018, 75),
  card: oklchToVar(0.96, 0.018, 82),
  muted: oklchToVar(0.89, 0.025, 82),
  mutedForeground: oklchToVar(0.45, 0.028, 74),
  border: oklchToVar(0.77, 0.03, 78),
  sidebar: oklchToVar(0.91, 0.029, 82),
  sidebarForeground: oklchToVar(0.3, 0.02, 75),
  sidebarActive: oklchToVar(0.86, 0.04, 256),
  sidebarActiveForeground: oklchToVar(0.28, 0.08, 256),
  chartColors: [
    oklchToVar(0.43, 0.13, 258),
    oklchToVar(0.53, 0.12, 148),
    oklchToVar(0.72, 0.13, 70),
    oklchToVar(0.54, 0.16, 28),
    oklchToVar(0.46, 0.06, 65),
  ],
});

const paperDarkTheme = createMaterialThemeColors({
  primary: oklchToVar(0.74, 0.12, 250),
  primaryForeground: oklchToVar(0.13, 0.018, 250),
  secondary: oklchToVar(0.27, 0.018, 70),
  accent: oklchToVar(0.34, 0.04, 74),
  success: oklchToVar(0.72, 0.12, 148),
  warning: oklchToVar(0.76, 0.13, 70),
  danger: oklchToVar(0.68, 0.16, 28),
  background: oklchToVar(0.16, 0.012, 70),
  foreground: oklchToVar(0.86, 0.024, 82),
  card: oklchToVar(0.22, 0.014, 70),
  muted: oklchToVar(0.29, 0.014, 70),
  mutedForeground: oklchToVar(0.66, 0.026, 82),
  border: oklchToVar(0.42, 0.02, 74),
  sidebar: oklchToVar(0.14, 0.012, 70),
  sidebarForeground: oklchToVar(0.82, 0.024, 82),
  sidebarActive: oklchToVar(0.29, 0.03, 250),
  sidebarActiveForeground: oklchToVar(0.88, 0.02, 250),
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

const ceramicTheme = createMaterialThemeColors({
  primary: oklchToVar(0.25, 0.006, 110),
  primaryForeground: oklchToVar(0.96, 0.006, 95),
  secondary: oklchToVar(0.9, 0.012, 95),
  secondaryForeground: oklchToVar(0.24, 0.006, 110),
  accent: oklchToVar(0.84, 0.015, 95),
  accentForeground: oklchToVar(0.24, 0.006, 110),
  success: oklchToVar(0.55, 0.09, 150),
  warning: oklchToVar(0.72, 0.1, 70),
  danger: oklchToVar(0.58, 0.14, 28),
  background: oklchToVar(0.95, 0.008, 95),
  foreground: oklchToVar(0.18, 0.004, 110),
  card: oklchToVar(0.98, 0.004, 95),
  muted: oklchToVar(0.9, 0.008, 95),
  mutedForeground: oklchToVar(0.44, 0.006, 110),
  border: oklchToVar(0.82, 0.008, 95),
  sidebar: oklchToVar(0.2, 0.005, 110),
  sidebarForeground: oklchToVar(0.92, 0.006, 95),
  sidebarActive: oklchToVar(0.35, 0.006, 110),
});

const ceramicDarkTheme = createMaterialThemeColors({
  primary: oklchToVar(0.78, 0.055, 205),
  primaryForeground: oklchToVar(0.1, 0.008, 205),
  secondary: oklchToVar(0.24, 0.008, 205),
  accent: oklchToVar(0.32, 0.02, 205),
  success: oklchToVar(0.72, 0.1, 150),
  warning: oklchToVar(0.78, 0.1, 70),
  danger: oklchToVar(0.7, 0.14, 28),
  background: oklchToVar(0.1, 0.006, 205),
  foreground: oklchToVar(0.88, 0.012, 205),
  card: oklchToVar(0.16, 0.008, 205),
  muted: oklchToVar(0.23, 0.008, 205),
  mutedForeground: oklchToVar(0.68, 0.014, 205),
  border: oklchToVar(0.37, 0.014, 205),
  sidebar: oklchToVar(0.08, 0.006, 205),
  sidebarForeground: oklchToVar(0.82, 0.012, 205),
  sidebarActive: oklchToVar(0.22, 0.014, 205),
});

const carbonTheme = createMaterialThemeColors({
  primary: oklchToVar(0.72, 0.14, 235),
  primaryForeground: oklchToVar(0.12, 0.02, 235),
  secondary: oklchToVar(0.23, 0.014, 240),
  accent: oklchToVar(0.34, 0.05, 230),
  success: oklchToVar(0.7, 0.15, 150),
  warning: oklchToVar(0.74, 0.15, 76),
  danger: oklchToVar(0.66, 0.17, 28),
  background: oklchToVar(0.12, 0.009, 240),
  foreground: oklchToVar(0.9, 0.01, 235),
  card: oklchToVar(0.17, 0.012, 240),
  muted: oklchToVar(0.24, 0.012, 240),
  mutedForeground: oklchToVar(0.68, 0.018, 235),
  border: oklchToVar(0.36, 0.025, 235),
  sidebar: oklchToVar(0.11, 0.008, 240),
  sidebarForeground: oklchToVar(0.84, 0.01, 235),
  sidebarActive: oklchToVar(0.24, 0.04, 235),
});

const carbonLightTheme = createMaterialThemeColors({
  primary: oklchToVar(0.5, 0.12, 235),
  primaryForeground: oklchToVar(0.98, 0.006, 235),
  secondary: oklchToVar(0.78, 0.012, 235),
  secondaryForeground: oklchToVar(0.19, 0.012, 235),
  accent: oklchToVar(0.82, 0.035, 230),
  accentForeground: oklchToVar(0.19, 0.012, 235),
  success: oklchToVar(0.53, 0.13, 150),
  warning: oklchToVar(0.66, 0.14, 76),
  danger: oklchToVar(0.58, 0.16, 28),
  background: oklchToVar(0.79, 0.008, 235),
  foreground: oklchToVar(0.18, 0.012, 235),
  card: oklchToVar(0.88, 0.008, 235),
  muted: oklchToVar(0.75, 0.01, 235),
  mutedForeground: oklchToVar(0.42, 0.018, 235),
  border: oklchToVar(0.58, 0.018, 235),
  sidebar: oklchToVar(0.76, 0.008, 235),
  sidebarForeground: oklchToVar(0.21, 0.012, 235),
  sidebarActive: oklchToVar(0.7, 0.025, 235),
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
  foreground: oklchToVar(0.86, 0.04, 82),
  card: oklchToVar(0.19, 0.018, 82),
  muted: oklchToVar(0.27, 0.022, 82),
  mutedForeground: oklchToVar(0.7, 0.04, 82),
  border: oklchToVar(0.48, 0.075, 78),
  sidebar: oklchToVar(0.13, 0.014, 82),
  sidebarForeground: oklchToVar(0.82, 0.04, 82),
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

const walnutWorkshopTheme = createMaterialThemeColors({
  primary: oklchToVar(0.43, 0.06, 118),
  primaryForeground: oklchToVar(0.96, 0.025, 82),
  secondary: oklchToVar(0.68, 0.065, 72),
  secondaryForeground: oklchToVar(0.23, 0.035, 62),
  accent: oklchToVar(0.75, 0.085, 78),
  accentForeground: oklchToVar(0.23, 0.035, 62),
  success: oklchToVar(0.51, 0.11, 140),
  warning: oklchToVar(0.65, 0.12, 72),
  danger: oklchToVar(0.51, 0.13, 30),
  background: oklchToVar(0.52, 0.065, 63),
  foreground: oklchToVar(0.2, 0.03, 58),
  card: oklchToVar(0.76, 0.065, 75),
  muted: oklchToVar(0.67, 0.06, 70),
  mutedForeground: oklchToVar(0.37, 0.04, 62),
  border: oklchToVar(0.39, 0.055, 62),
  input: oklchToVar(0.58, 0.06, 68),
  sidebar: oklchToVar(0.53, 0.065, 64),
  sidebarForeground: oklchToVar(0.22, 0.03, 58),
  sidebarActive: oklchToVar(0.32, 0.035, 105),
  sidebarActiveForeground: oklchToVar(0.91, 0.04, 82),
  ring: oklchToVar(0.46, 0.075, 118),
  chartColors: [
    oklchToVar(0.43, 0.06, 118),
    oklchToVar(0.5, 0.08, 68),
    oklchToVar(0.51, 0.11, 140),
    oklchToVar(0.65, 0.12, 72),
    oklchToVar(0.51, 0.13, 30),
  ],
});

const walnutWorkshopDarkTheme = createMaterialThemeColors({
  primary: oklchToVar(0.72, 0.1, 118),
  primaryForeground: oklchToVar(0.16, 0.025, 82),
  secondary: oklchToVar(0.29, 0.04, 72),
  secondaryForeground: oklchToVar(0.9, 0.035, 82),
  accent: oklchToVar(0.42, 0.06, 78),
  accentForeground: oklchToVar(0.92, 0.035, 82),
  success: oklchToVar(0.7, 0.12, 140),
  warning: oklchToVar(0.76, 0.13, 72),
  danger: oklchToVar(0.68, 0.15, 30),
  background: oklchToVar(0.16, 0.025, 63),
  foreground: oklchToVar(0.9, 0.035, 82),
  card: oklchToVar(0.23, 0.03, 70),
  muted: oklchToVar(0.3, 0.03, 70),
  mutedForeground: oklchToVar(0.7, 0.035, 75),
  border: oklchToVar(0.43, 0.05, 68),
  input: oklchToVar(0.27, 0.03, 68),
  sidebar: oklchToVar(0.13, 0.02, 64),
  sidebarForeground: oklchToVar(0.84, 0.035, 82),
  sidebarActive: oklchToVar(0.32, 0.05, 105),
  sidebarActiveForeground: oklchToVar(0.94, 0.03, 82),
  ring: oklchToVar(0.72, 0.1, 118),
  chartColors: [
    oklchToVar(0.72, 0.1, 118),
    oklchToVar(0.7, 0.11, 68),
    oklchToVar(0.7, 0.12, 140),
    oklchToVar(0.76, 0.13, 72),
    oklchToVar(0.68, 0.15, 30),
  ],
});

export const materialThemeIds = ["stone", "paper", "blueprint", "ceramic", "carbon", "brass-instrument", "walnut-workshop"] as const;

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

export const builtInThemeDefinitions: Theme[] = [
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
    "paper",
    "Paper",
    "Layered vellum, inked diagrams, and stamped review notes.",
    { ...paperTheme, material: materialMode("vellum", "/studio/assets/paper-vellum-tile.png", 0.32, 430) },
    { ...paperDarkTheme, material: materialMode("charcoal-paper", "/studio/assets/paper-charcoal-tile.png", 0.64, 430) },
    ["light"]
  ),
  createThemeDefinition(
    "blueprint",
    "Blueprint",
    "Architectural drafting paper with crisp cyan workflow marks.",
    { ...blueprintLightTheme, material: materialMode("drafting-paper", "/studio/assets/blueprint-drafting-tile.png", 0.16, 720) },
    { ...blueprintTheme, material: materialMode("blueprint-paper", "/studio/assets/blueprint-paper-tile.png", 0.7, 420) }
  ),
  createThemeDefinition(
    "ceramic",
    "Ceramic",
    "Matte porcelain surfaces with soft glazed controls.",
    { ...ceramicTheme, material: materialMode("porcelain", "/studio/assets/ceramic-glaze-tile.png", 0.3, 420) },
    { ...ceramicDarkTheme, material: materialMode("obsidian-glaze", "/studio/assets/ceramic-obsidian-tile.png", 0.28, 780) }
  ),
  createThemeDefinition(
    "carbon",
    "Carbon",
    "Technical carbon surface with a precise cyan working accent.",
    { ...carbonLightTheme, material: materialMode("silver-carbon", "/studio/assets/carbon-silver-tile.png", 0.08, 720) },
    { ...carbonTheme, material: materialMode("carbon-weave", "/studio/assets/carbon-weave-tile.png", 0.08, 720) }
  ),
  createThemeDefinition(
    "brass-instrument",
    "Brass Instrument",
    "Dark enamel panels with machined brass controls.",
    { ...brassLightTheme, material: materialMode("champagne-brass", "/studio/assets/brass-champagne-tile.png", 0.42, 380) },
    { ...brassTheme, material: materialMode("brass-enamel", "/studio/assets/brass-enamel-tile.png", 0.78, 380) },
    ["dark"]
  ),
  createThemeDefinition(
    "walnut-workshop",
    "Walnut Workshop",
    "Warm walnut panels, parchment work surfaces, and aged brass details.",
    { ...walnutWorkshopTheme, material: materialMode("waxed-walnut", "/studio/assets/walnut-workshop-tile.png", 0.64, 720) },
    { ...walnutWorkshopDarkTheme, material: materialMode("waxed-walnut", "/studio/assets/walnut-workshop-tile.png", 0.64, 720) },
    ["light"]
  ),
  createThemeDefinition("harbor", "Harbor", "Crisp blue for operational dashboards.", createMode(oklchToVar(0.68, 0.16, 235), 235), createMode(oklchToVar(0.6, 0.16, 235), 235, { background: oklchToVar(0.18, 0.02, 250) })),
  createThemeDefinition("borealis", "Borealis", "Green-teal palette with a calm technical feel.", createMode(oklchToVar(0.72, 0.14, 168), 168), createMode(oklchToVar(0.62, 0.14, 168), 168, { background: oklchToVar(0.17, 0.02, 168) })),
  createThemeDefinition("ember", "Ember", "Warm amber palette for high-contrast highlights.", createMode(oklchToVar(0.74, 0.17, 58), 58), createMode(oklchToVar(0.64, 0.17, 58), 58, { background: oklchToVar(0.18, 0.02, 58) })),
  createThemeDefinition("orchid", "Orchid", "Refined violet palette for expressive workspaces.", createMode(oklchToVar(0.66, 0.17, 292), 292), createMode(oklchToVar(0.58, 0.16, 292), 292, { background: oklchToVar(0.18, 0.02, 292) })),
  createThemeDefinition("hot-pink", "Hot Pink", "High-energy pink palette for bold workspaces.", createMode(oklchToVar(0.7, 0.24, 340), 340), createMode(oklchToVar(0.64, 0.22, 340), 340, { background: oklchToVar(0.18, 0.02, 340) })),
  createThemeDefinition("coral", "Coral", "Soft red-coral palette with readable emphasis.", createMode(oklchToVar(0.68, 0.18, 24), 24), createMode(oklchToVar(0.58, 0.17, 24), 24, { background: oklchToVar(0.18, 0.02, 24) })),
  createThemeDefinition("graphite", "Graphite", "Restrained neutral palette with a cool accent.", createMode(oklchToVar(0.6, 0.04, 248), 248), createMode(oklchToVar(0.52, 0.04, 248), 248, { background: oklchToVar(0.18, 0.02, 248) }))
];

export const themes: Theme[] = builtInThemeDefinitions;

export const getTheme = (themeId: string): Theme | undefined =>
  themes.find(t => t.id === themeId);

export const getThemeNames = (): { id: string; name: string }[] =>
  themes.map(t => ({ id: t.id, name: t.name }));

function getOklchLightness(value: string) {
  const match = value.match(/^oklch\((0?\.\d+|1(?:\.0+)?)\s/i);
  return match ? Number(match[1]) : 1;
}
