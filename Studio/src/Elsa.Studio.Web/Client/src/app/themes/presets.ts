export type StudioThemeSource = "built-in" | "custom";
export type ThemeMode = "light" | "dark";

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
  dark: ThemeModeDefinition
): Theme {
  const definition: StudioThemeDefinition = {
    id,
    name,
    description,
    source: "built-in",
    version: 1,
    enabled: true,
    published: true,
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
    light: definition.modes.light,
    dark: definition.modes.dark
  };
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
    modes: theme.modes,
    material: theme.material
  })) as StudioThemeDefinition;
}

export const builtInThemeDefinitions: Theme[] = [
  createThemeDefinition(
    "black-glass",
    "Black Glass",
    "Glossy dark material with frosted light-mode contrast.",
    createMode(oklchToVar(0.58, 0.08, 245), 245, {
      background: oklchToVar(0.94, 0.006, 245),
      card: oklchToVar(0.98, 0.004, 245),
      material: { cssVariables: { "--studio-material-finish": "frosted", "--studio-material-depth": "0.36" } }
    }),
    createMode(oklchToVar(0.7, 0.11, 250), 250, {
      background: oklchToVar(0.12, 0.014, 250),
      card: oklchToVar(0.19, 0.018, 250),
      border: oklchToVar(0.33, 0.02, 250),
      material: { cssVariables: { "--studio-material-finish": "glass", "--studio-material-depth": "0.72" } }
    })
  ),
  createThemeDefinition(
    "stone",
    "Stone",
    "Grounded limestone and basalt variants for long operational sessions.",
    createMode(oklchToVar(0.55, 0.04, 80), 80, {
      background: oklchToVar(0.93, 0.015, 85),
      card: oklchToVar(0.98, 0.009, 85)
    }),
    createMode(oklchToVar(0.63, 0.05, 70), 70, {
      background: oklchToVar(0.18, 0.01, 75),
      card: oklchToVar(0.25, 0.012, 75)
    })
  ),
  createThemeDefinition(
    "paper",
    "Paper",
    "Warm drafting paper in light mode with archival ink in dark mode.",
    createMode(oklchToVar(0.6, 0.09, 70), 70, {
      background: oklchToVar(0.97, 0.025, 78),
      card: oklchToVar(0.995, 0.014, 78)
    }),
    createMode(oklchToVar(0.72, 0.09, 78), 78, {
      background: oklchToVar(0.19, 0.018, 72),
      card: oklchToVar(0.25, 0.02, 72)
    })
  ),
  createThemeDefinition(
    "blueprint",
    "Blueprint",
    "Classic blueprint and light drafting-board material variants.",
    createMode(oklchToVar(0.56, 0.12, 235), 235, {
      background: oklchToVar(0.96, 0.018, 230),
      card: oklchToVar(0.99, 0.01, 230)
    }),
    createMode(oklchToVar(0.68, 0.15, 235), 235, {
      background: oklchToVar(0.17, 0.045, 245),
      card: oklchToVar(0.23, 0.05, 245)
    })
  ),
  createThemeDefinition(
    "ceramic",
    "Ceramic",
    "White ceramic and dark glazed ceramic with restrained status colors.",
    createMode(oklchToVar(0.62, 0.07, 190), 190, {
      background: oklchToVar(0.985, 0.006, 190),
      card: oklchToVar(1, 0, 0)
    }),
    createMode(oklchToVar(0.68, 0.08, 190), 190, {
      background: oklchToVar(0.16, 0.02, 195),
      card: oklchToVar(0.22, 0.025, 195)
    })
  ),
  createThemeDefinition(
    "carbon",
    "Carbon",
    "Technical carbon surface with a precise cyan working accent.",
    createMode(oklchToVar(0.56, 0.08, 220), 220, {
      background: oklchToVar(0.95, 0.006, 220),
      card: oklchToVar(0.99, 0.004, 220)
    }),
    createMode(oklchToVar(0.74, 0.11, 220), 220, {
      background: oklchToVar(0.13, 0.012, 230),
      card: oklchToVar(0.2, 0.014, 230)
    })
  ),
  createThemeDefinition(
    "brass-instrument",
    "Brass Instrument",
    "Instrument-panel brass accents with dark workshop and light bench variants.",
    createMode(oklchToVar(0.66, 0.12, 78), 78, {
      background: oklchToVar(0.95, 0.018, 78),
      card: oklchToVar(0.99, 0.01, 78)
    }),
    createMode(oklchToVar(0.73, 0.14, 78), 78, {
      background: oklchToVar(0.18, 0.018, 62),
      card: oklchToVar(0.24, 0.02, 62)
    })
  ),
  createThemeDefinition("harbor", "Harbor", "Crisp blue for operational dashboards.", createMode(oklchToVar(0.68, 0.16, 235), 235), createMode(oklchToVar(0.6, 0.16, 235), 235, { background: oklchToVar(0.18, 0.02, 250) })),
  createThemeDefinition("borealis", "Borealis", "Green-teal palette with a calm technical feel.", createMode(oklchToVar(0.72, 0.14, 168), 168), createMode(oklchToVar(0.62, 0.14, 168), 168, { background: oklchToVar(0.17, 0.02, 168) })),
  createThemeDefinition("hot-pink", "Hot Pink", "High-energy pink palette for bold workspaces.", createMode(oklchToVar(0.7, 0.24, 340), 340), createMode(oklchToVar(0.64, 0.22, 340), 340, { background: oklchToVar(0.18, 0.02, 340) }))
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
