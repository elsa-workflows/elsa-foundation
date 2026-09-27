import type { StudioThemeDefinition, ThemeModeDefinition, ThemeTypography } from "./presets";

/*
 * The signature Elsa Studio 4 themes. Each defines all four modes — Light, Dark, Dim and High
 * contrast — plus its own typography and corner radii. Elevation (shadow) recipes, which are not
 * colour tokens, live beside them in ui/tokens.css under `html[data-theme="<id>"]`.
 *
 * This module only builds plain definitions (no calls into presets.ts) so the two can import each
 * other's types without an evaluation-order cycle.
 */

const oklch = (lightness: number, chroma: number, hue: number) => `oklch(${lightness} ${chroma} ${hue})`;
const black = oklch(0, 0, 0);
const white = oklch(0.99, 0, 0);

type Scheme = "light" | "dark" | "dim" | "high-contrast";

/** Status fills and the text that sits on them, tuned per mode for AA on the fill. */
const status: Record<Scheme, Pick<ThemeModeDefinition,
  "success" | "successForeground" | "warning" | "warningForeground" | "danger" | "dangerForeground">> = {
  light: {
    success: oklch(0.53, 0.14, 150), successForeground: white,
    warning: oklch(0.76, 0.16, 70), warningForeground: oklch(0.22, 0.04, 70),
    danger: oklch(0.56, 0.2, 27), dangerForeground: white
  },
  dark: {
    success: oklch(0.72, 0.15, 150), successForeground: oklch(0.17, 0.03, 150),
    warning: oklch(0.79, 0.15, 75), warningForeground: oklch(0.19, 0.04, 70),
    danger: oklch(0.67, 0.19, 25), dangerForeground: oklch(0.16, 0.03, 25)
  },
  dim: {
    success: oklch(0.75, 0.14, 150), successForeground: oklch(0.18, 0.03, 150),
    warning: oklch(0.82, 0.14, 75), warningForeground: oklch(0.2, 0.04, 70),
    danger: oklch(0.7, 0.18, 25), dangerForeground: oklch(0.17, 0.03, 25)
  },
  "high-contrast": {
    success: oklch(0.86, 0.19, 150), successForeground: black,
    warning: oklch(0.9, 0.17, 95), warningForeground: black,
    danger: oklch(0.74, 0.2, 25), dangerForeground: black
  }
};

/** Chart series: the theme accent, then one hue per activity family, at a mode-appropriate lightness. */
const chartLightness: Record<Scheme, [number, number]> = {
  light: [0.6, 0.16],
  dark: [0.72, 0.14],
  dim: [0.75, 0.13],
  "high-contrast": [0.85, 0.17]
};

interface ModeSpec {
  scheme: Scheme;
  /** Neutral ladder hue and chroma; text uses at most 0.015 chroma so it never reads tinted. */
  hue: number;
  chroma: number;
  background: number;
  card: number;
  muted: number;
  border: number;
  input: number;
  sidebar: number;
  foreground: number;
  mutedForeground: number;
  primary: string;
  primaryForeground: string;
  /** Opaque accent wash used for hover rows and the active navigation item. */
  wash: string;
  activeForeground: string;
  overrides?: Partial<ThemeModeDefinition>;
}

function mode(spec: ModeSpec): ThemeModeDefinition {
  const surface = (lightness: number) => oklch(lightness, lightness === 0 || lightness === 1 ? 0 : spec.chroma, spec.hue);
  const text = (lightness: number) => oklch(lightness, Math.min(spec.chroma, 0.015), spec.hue);
  const foreground = text(spec.foreground);
  const [chartL, chartC] = chartLightness[spec.scheme];

  return {
    primary: spec.primary,
    primaryForeground: spec.primaryForeground,
    secondary: surface(spec.muted),
    secondaryForeground: foreground,
    accent: spec.wash,
    accentForeground: foreground,
    ...status[spec.scheme],
    background: surface(spec.background),
    foreground,
    card: surface(spec.card),
    cardForeground: foreground,
    muted: surface(spec.muted),
    mutedForeground: text(spec.mutedForeground),
    border: surface(spec.border),
    input: surface(spec.input),
    sidebar: surface(spec.sidebar),
    sidebarForeground: foreground,
    sidebarActive: spec.wash,
    sidebarActiveForeground: spec.activeForeground,
    ring: spec.primary,
    chartColors: [spec.primary, ...[155, 75, 295, 20].map(hue => oklch(chartL, chartC, hue))],
    ...spec.overrides
  };
}

/**
 * High contrast is deliberately shared in shape across themes: pure black ground and cards, white
 * hairlines, white text and a single bright accent that also marks the active navigation item.
 * Depth comes from borders alone; the only lifted tier is the recessed `muted` surface.
 * Only the accent changes, so each theme keeps its identity without trading away legibility.
 */
function highContrast(accent: string, accentHue: number): ThemeModeDefinition {
  return mode({
    scheme: "high-contrast",
    hue: 0,
    chroma: 0,
    background: 0,
    card: 0,
    muted: 0.16,
    border: 1,
    input: 0,
    sidebar: 0,
    foreground: 1,
    mutedForeground: 0.9,
    primary: accent,
    primaryForeground: black,
    wash: oklch(0.3, 0.07, accentHue),
    activeForeground: black,
    overrides: { sidebarActive: accent, accentForeground: oklch(1, 0, 0) }
  });
}

const monoFallback = 'ui-monospace, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace';
const sansFallback = '"Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, sans-serif';

function definition(
  id: string,
  name: string,
  description: string,
  typography: ThemeTypography,
  shape: StudioThemeDefinition["shape"],
  modes: Required<StudioThemeDefinition["modes"]>
): StudioThemeDefinition {
  return {
    id,
    name,
    description,
    source: "built-in",
    version: 1,
    enabled: true,
    published: true,
    supportedModes: ["light", "dark", "dim", "high-contrast"],
    typography,
    shape,
    modes
  };
}

const meridian = definition(
  "meridian",
  "Meridian",
  "Crisp, neutral precision with a single indigo accent. The Elsa Studio 4 default.",
  {
    sans: `"Geist Variable", "Geist", ${sansFallback}`,
    mono: `"Geist Mono Variable", "Geist Mono", ${monoFallback}`
  },
  { radiusSm: "5px", radius: "7px", radiusMd: "9px", radiusLg: "12px", radiusXl: "14px" },
  {
    light: mode({
      scheme: "light", hue: 265, chroma: 0.006,
      background: 0.985, card: 1, muted: 0.965, border: 0.91, input: 1, sidebar: 1, foreground: 0.22, mutedForeground: 0.49,
      primary: oklch(0.52, 0.2, 268), primaryForeground: white,
      wash: oklch(0.95, 0.035, 268), activeForeground: oklch(0.45, 0.2, 268)
    }),
    dark: mode({
      scheme: "dark", hue: 265, chroma: 0.008,
      background: 0.155, card: 0.185, muted: 0.225, border: 0.28, input: 0.17, sidebar: 0.185, foreground: 0.95, mutedForeground: 0.72,
      primary: oklch(0.56, 0.19, 268), primaryForeground: white,
      wash: oklch(0.29, 0.07, 268), activeForeground: oklch(0.82, 0.1, 268)
    }),
    dim: mode({
      scheme: "dim", hue: 262, chroma: 0.016,
      background: 0.265, card: 0.295, muted: 0.335, border: 0.38, input: 0.28, sidebar: 0.295, foreground: 0.94, mutedForeground: 0.78,
      primary: oklch(0.55, 0.17, 268), primaryForeground: white,
      wash: oklch(0.37, 0.07, 268), activeForeground: oklch(0.86, 0.08, 268)
    }),
    highContrast: highContrast(oklch(0.91, 0.18, 100), 100)
  }
);

const drift = definition(
  "drift",
  "Drift",
  "Soft, rounded surfaces that float over a calm canvas, with a jade accent.",
  {
    sans: `"Manrope Variable", "Manrope", ${sansFallback}`,
    mono: `"DM Mono", ${monoFallback}`
  },
  { radiusSm: "8px", radius: "12px", radiusMd: "14px", radiusLg: "18px", radiusXl: "22px" },
  {
    light: mode({
      scheme: "light", hue: 195, chroma: 0.008,
      background: 0.958, card: 0.997, muted: 0.95, border: 0.915, input: 0.955, sidebar: 0.997, foreground: 0.23, mutedForeground: 0.49,
      primary: oklch(0.5, 0.1, 172), primaryForeground: white,
      wash: oklch(0.94, 0.04, 172), activeForeground: oklch(0.42, 0.1, 172)
    }),
    dark: mode({
      scheme: "dark", hue: 200, chroma: 0.013,
      background: 0.17, card: 0.215, muted: 0.25, border: 0.29, input: 0.25, sidebar: 0.215, foreground: 0.95, mutedForeground: 0.74,
      primary: oklch(0.74, 0.12, 172), primaryForeground: oklch(0.19, 0.03, 180),
      wash: oklch(0.3, 0.05, 172), activeForeground: oklch(0.84, 0.11, 172)
    }),
    dim: mode({
      scheme: "dim", hue: 205, chroma: 0.022,
      background: 0.3, card: 0.345, muted: 0.38, border: 0.42, input: 0.38, sidebar: 0.345, foreground: 0.96, mutedForeground: 0.81,
      primary: oklch(0.77, 0.12, 172), primaryForeground: oklch(0.2, 0.03, 180),
      wash: oklch(0.4, 0.05, 172), activeForeground: oklch(0.88, 0.1, 172)
    }),
    highContrast: highContrast(oklch(0.88, 0.15, 175), 175)
  }
);

const schematic = definition(
  "schematic",
  "Schematic",
  "An engineering workbench: squared corners, blueprint neutrals and a signal-orange accent.",
  {
    sans: `"IBM Plex Sans", ${sansFallback}`,
    mono: `"IBM Plex Mono", ${monoFallback}`
  },
  { radiusSm: "2px", radius: "3px", radiusMd: "4px", radiusLg: "4px", radiusXl: "6px" },
  {
    light: mode({
      scheme: "light", hue: 245, chroma: 0.014,
      background: 0.962, card: 0.992, muted: 0.95, border: 0.86, input: 0.992, sidebar: 0.992, foreground: 0.24, mutedForeground: 0.46,
      primary: oklch(0.57, 0.19, 42), primaryForeground: white,
      wash: oklch(0.93, 0.02, 245), activeForeground: oklch(0.24, 0.04, 255)
    }),
    dark: mode({
      scheme: "dark", hue: 255, chroma: 0.026,
      background: 0.17, card: 0.205, muted: 0.24, border: 0.3, input: 0.185, sidebar: 0.205, foreground: 0.94, mutedForeground: 0.73,
      primary: oklch(0.74, 0.16, 58), primaryForeground: oklch(0.19, 0.04, 55),
      wash: oklch(0.28, 0.035, 255), activeForeground: oklch(0.94, 0.01, 240)
    }),
    // Dim is a cyanotype: saturated blueprint blue with near-white linework.
    dim: mode({
      scheme: "dim", hue: 258, chroma: 0.085,
      background: 0.3, card: 0.335, muted: 0.37, border: 0.45, input: 0.315, sidebar: 0.335, foreground: 0.97, mutedForeground: 0.86,
      primary: oklch(0.82, 0.15, 78), primaryForeground: oklch(0.22, 0.05, 70),
      wash: oklch(0.42, 0.08, 258), activeForeground: oklch(0.97, 0.01, 240)
    }),
    highContrast: highContrast(oklch(0.82, 0.17, 65), 65)
  }
);

const atelierSans = `"Instrument Sans Variable", "Instrument Sans", ${sansFallback}`;

const atelier = definition(
  "atelier",
  "Atelier",
  "Warm, editorial calm: paper neutrals, serif titles and a plum accent.",
  {
    sans: atelierSans,
    display: `"Instrument Serif", Georgia, "Times New Roman", serif`,
    mono: `"JetBrains Mono Variable", "JetBrains Mono", ${monoFallback}`
  },
  { radiusSm: "6px", radius: "9px", radiusMd: "10px", radiusLg: "14px", radiusXl: "16px" },
  {
    light: mode({
      scheme: "light", hue: 78, chroma: 0.012,
      background: 0.955, card: 0.993, muted: 0.952, border: 0.895, input: 0.993, sidebar: 0.955, foreground: 0.23, mutedForeground: 0.48,
      primary: oklch(0.45, 0.13, 350), primaryForeground: white,
      wash: oklch(0.94, 0.025, 350), activeForeground: oklch(0.23, 0.015, 78),
      overrides: { sidebarActive: oklch(0.993, 0.012, 78) }
    }),
    dark: mode({
      scheme: "dark", hue: 60, chroma: 0.009,
      background: 0.155, card: 0.2, muted: 0.235, border: 0.285, input: 0.18, sidebar: 0.155, foreground: 0.94, mutedForeground: 0.74,
      primary: oklch(0.72, 0.12, 350), primaryForeground: oklch(0.19, 0.04, 350),
      wash: oklch(0.29, 0.04, 350), activeForeground: oklch(0.94, 0.009, 60),
      overrides: { sidebarActive: oklch(0.245, 0.009, 60) }
    }),
    dim: mode({
      scheme: "dim", hue: 55, chroma: 0.016,
      background: 0.27, card: 0.32, muted: 0.355, border: 0.395, input: 0.3, sidebar: 0.27, foreground: 0.95, mutedForeground: 0.8,
      primary: oklch(0.77, 0.1, 350), primaryForeground: oklch(0.2, 0.04, 350),
      wash: oklch(0.38, 0.04, 350), activeForeground: oklch(0.95, 0.015, 55),
      overrides: { sidebarActive: oklch(0.365, 0.016, 55) }
    }),
    highContrast: highContrast(oklch(0.86, 0.12, 345), 345)
  }
);

export const foundationThemeIds = ["meridian", "drift", "schematic", "atelier"] as const;

export const foundationThemeDefinitions: StudioThemeDefinition[] = [meridian, drift, schematic, atelier];
