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

export const themes: Theme[] = [
  createTheme('harbor', 'Harbor', 'Crisp blue for operational dashboards', oklchToVar(0.68, 0.16, 235), oklchToVar(0.6, 0.16, 235), 235),
  {
    id: 'black-glass',
    name: 'Black Glass',
    description: 'Smoked-glass Studio surfaces with cyan blueprint glow',
    light: blackGlassLight,
    dark: blackGlassDark,
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
