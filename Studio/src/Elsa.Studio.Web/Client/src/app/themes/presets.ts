export interface ThemeColors {
  // Semantic colors
  primary: string;
  primaryForeground: string;
  secondary: string;
  secondaryForeground: string;
  accent: string;
  accentForeground: string;
  
  // Status colors
  success: string;
  successForeground: string;
  warning: string;
  warningForeground: string;
  danger: string;
  dangerForeground: string;
  
  // UI colors
  background: string;
  foreground: string;
  card: string;
  cardForeground: string;
  muted: string;
  mutedForeground: string;
  border: string;
  input: string;
  
  // Sidebar colors
  sidebar: string;
  sidebarForeground: string;
  sidebarActive: string;
  sidebarActiveForeground: string;
  
  // Additional
  ring: string;
  chartColors: string[];
}

export interface Theme {
  id: string;
  name: string;
  description: string;
  light: ThemeColors;
  dark: ThemeColors;
}

const oklchToVar = (lightness: number, chroma: number, hue: number): string => 
  `oklch(${lightness} ${chroma} ${hue})`;

// Theme factory to create light and dark variants
const createTheme = (
  id: string,
  name: string,
  description: string,
  lightPrimary: string,
  darkPrimary: string,
  accentHue: number
): Theme => ({
  id,
  name,
  description,
  light: {
    primary: lightPrimary,
    primaryForeground: oklchToVar(0.985, 0, 0),
    secondary: oklchToVar(0.97, 0, 0),
    secondaryForeground: oklchToVar(0.205, 0, 0),
    accent: oklchToVar(0.96, 0.02, accentHue),
    accentForeground: oklchToVar(0.205, 0, 0),
    
    success: oklchToVar(0.75, 0.16, 150),
    successForeground: oklchToVar(0.985, 0, 0),
    warning: oklchToVar(0.72, 0.18, 60),
    warningForeground: oklchToVar(0.985, 0, 0),
    danger: oklchToVar(0.577, 0.245, 27),
    dangerForeground: oklchToVar(0.985, 0, 0),
    
    background: oklchToVar(1, 0, 0),
    foreground: oklchToVar(0.205, 0, 0),
    card: oklchToVar(1, 0, 0),
    cardForeground: oklchToVar(0.205, 0, 0),
    muted: oklchToVar(0.97, 0, 0),
    mutedForeground: oklchToVar(0.5, 0, 0),
    border: oklchToVar(0.92, 0, 0),
    input: oklchToVar(0.92, 0, 0),
    
    sidebar: oklchToVar(0.985, 0.005, 240),
    sidebarForeground: oklchToVar(0.25, 0, 0),
    sidebarActive: oklchToVar(0.95, 0.02, accentHue),
    sidebarActiveForeground: oklchToVar(0.205, 0, 0),
    
    ring: lightPrimary,
    chartColors: [lightPrimary, oklchToVar(0.62, 0.18, 264), oklchToVar(0.74, 0.12, 175), oklchToVar(0.72, 0.18, 60), oklchToVar(0.62, 0.22, 305)],
  },
  dark: {
    primary: darkPrimary,
    primaryForeground: oklchToVar(0.985, 0, 0),
    secondary: oklchToVar(0.25, 0.02, 240),
    secondaryForeground: oklchToVar(0.97, 0, 0),
    accent: oklchToVar(0.3, 0.03, accentHue),
    accentForeground: oklchToVar(0.97, 0, 0),
    
    success: oklchToVar(0.65, 0.16, 150),
    successForeground: oklchToVar(0.15, 0, 0),
    warning: oklchToVar(0.62, 0.18, 60),
    warningForeground: oklchToVar(0.15, 0, 0),
    danger: oklchToVar(0.477, 0.245, 27),
    dangerForeground: oklchToVar(0.15, 0, 0),
    
    background: oklchToVar(0.18, 0.02, 250),
    foreground: oklchToVar(0.97, 0, 0),
    card: oklchToVar(0.25, 0.02, 250),
    cardForeground: oklchToVar(0.97, 0, 0),
    muted: oklchToVar(0.3, 0, 0),
    mutedForeground: oklchToVar(0.7, 0, 0),
    border: oklchToVar(0.35, 0.01, 250),
    input: oklchToVar(0.3, 0.01, 250),
    
    sidebar: oklchToVar(0.15, 0.02, 250),
    sidebarForeground: oklchToVar(0.85, 0, 0),
    sidebarActive: oklchToVar(0.3, 0.03, accentHue),
    sidebarActiveForeground: oklchToVar(0.97, 0, 0),
    
    ring: darkPrimary,
    chartColors: [darkPrimary, oklchToVar(0.52, 0.18, 264), oklchToVar(0.64, 0.12, 175), oklchToVar(0.62, 0.18, 60), oklchToVar(0.52, 0.22, 305)],
  },
});

const blackGlassDark: ThemeColors = {
  primary: oklchToVar(0.78, 0.16, 235),
  primaryForeground: oklchToVar(0.12, 0.02, 245),
  secondary: oklchToVar(0.22, 0.02, 245),
  secondaryForeground: oklchToVar(0.94, 0.01, 245),
  accent: oklchToVar(0.34, 0.05, 235),
  accentForeground: oklchToVar(0.96, 0.01, 235),

  success: oklchToVar(0.78, 0.16, 156),
  successForeground: oklchToVar(0.12, 0.02, 156),
  warning: oklchToVar(0.78, 0.14, 70),
  warningForeground: oklchToVar(0.12, 0.02, 70),
  danger: oklchToVar(0.72, 0.17, 18),
  dangerForeground: oklchToVar(0.12, 0.02, 18),

  background: oklchToVar(0.13, 0.014, 245),
  foreground: oklchToVar(0.93, 0.012, 235),
  card: oklchToVar(0.18, 0.018, 245),
  cardForeground: oklchToVar(0.93, 0.012, 235),
  muted: oklchToVar(0.24, 0.018, 245),
  mutedForeground: oklchToVar(0.72, 0.025, 235),
  border: oklchToVar(0.42, 0.055, 235),
  input: oklchToVar(0.19, 0.018, 245),

  sidebar: oklchToVar(0.11, 0.014, 245),
  sidebarForeground: oklchToVar(0.86, 0.018, 235),
  sidebarActive: oklchToVar(0.24, 0.055, 235),
  sidebarActiveForeground: oklchToVar(0.97, 0.012, 235),

  ring: oklchToVar(0.78, 0.16, 235),
  chartColors: [
    oklchToVar(0.78, 0.16, 235),
    oklchToVar(0.76, 0.14, 198),
    oklchToVar(0.78, 0.16, 156),
    oklchToVar(0.78, 0.14, 70),
    oklchToVar(0.72, 0.17, 18),
  ],
};

const blackGlassLight: ThemeColors = {
  ...blackGlassDark,
  background: oklchToVar(0.16, 0.016, 245),
  card: oklchToVar(0.21, 0.018, 245),
  muted: oklchToVar(0.28, 0.02, 245),
  border: oklchToVar(0.48, 0.06, 235),
  input: oklchToVar(0.22, 0.018, 245),
  sidebar: oklchToVar(0.13, 0.016, 245),
  sidebarActive: oklchToVar(0.28, 0.06, 235),
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

export const materialThemeIds = ["stone", "paper", "blueprint", "ceramic", "carbon", "brass-instrument"] as const;

export const isMaterialTheme = (themeId: string): boolean =>
  (materialThemeIds as readonly string[]).includes(themeId);

export const themes: Theme[] = [
  createTheme('harbor', 'Harbor', 'Crisp blue for operational dashboards', oklchToVar(0.68, 0.16, 235), oklchToVar(0.6, 0.16, 235), 235),
  {
    id: 'black-glass',
    name: 'Black Glass',
    description: 'Smoked-glass Studio surfaces with cyan blueprint glow',
    light: blackGlassLight,
    dark: blackGlassDark,
  },
  {
    id: 'stone',
    name: 'Stone',
    description: 'Carved slate surfaces with etched workflow lines',
    light: stoneTheme,
    dark: stoneTheme,
  },
  {
    id: 'paper',
    name: 'Paper',
    description: 'Layered vellum, inked diagrams, and stamped review notes',
    light: paperTheme,
    dark: paperTheme,
  },
  {
    id: 'blueprint',
    name: 'Blueprint',
    description: 'Architectural drafting paper with crisp cyan workflow marks',
    light: blueprintTheme,
    dark: blueprintTheme,
  },
  {
    id: 'ceramic',
    name: 'Ceramic',
    description: 'Matte porcelain surfaces with soft glazed controls',
    light: ceramicTheme,
    dark: ceramicTheme,
  },
  {
    id: 'carbon',
    name: 'Carbon',
    description: 'Carbon-fiber operations console with luminous telemetry',
    light: carbonTheme,
    dark: carbonTheme,
  },
  {
    id: 'brass-instrument',
    name: 'Brass Instrument',
    description: 'Dark enamel panels with machined brass controls',
    light: brassTheme,
    dark: brassTheme,
  },
  createTheme('borealis', 'Borealis', 'Green-teal palette with a calm technical feel', oklchToVar(0.72, 0.14, 168), oklchToVar(0.62, 0.14, 168), 168),
  createTheme('ember', 'Ember', 'Warm amber palette for high-contrast highlights', oklchToVar(0.74, 0.17, 58), oklchToVar(0.64, 0.17, 58), 58),
  createTheme('orchid', 'Orchid', 'Refined violet palette for expressive workspaces', oklchToVar(0.66, 0.17, 292), oklchToVar(0.58, 0.16, 292), 292),
  createTheme('hot-pink', 'Hot Pink', 'High-energy pink palette for bold workspaces', oklchToVar(0.7, 0.24, 340), oklchToVar(0.64, 0.22, 340), 340),
  createTheme('coral', 'Coral', 'Soft red-coral palette with readable emphasis', oklchToVar(0.68, 0.18, 24), oklchToVar(0.58, 0.17, 24), 24),
  createTheme('graphite', 'Graphite', 'Restrained neutral palette with a cool accent', oklchToVar(0.6, 0.04, 248), oklchToVar(0.52, 0.04, 248), 248),
];

export const getTheme = (themeId: string): Theme | undefined => 
  themes.find(t => t.id === themeId);

export const getThemeNames = (): { id: string; name: string }[] =>
  themes.map(t => ({ id: t.id, name: t.name }));
