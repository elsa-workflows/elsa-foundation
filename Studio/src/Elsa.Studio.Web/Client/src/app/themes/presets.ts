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

export const themes: Theme[] = [
  // Blues & Cyans
  createTheme('cyan', 'Cyan', 'Vibrant cyan and blue palette', oklchToVar(0.685, 0.156, 237), oklchToVar(0.585, 0.156, 237), 237),
  createTheme('sky', 'Sky', 'Light sky blue palette', oklchToVar(0.72, 0.14, 236), oklchToVar(0.62, 0.14, 236), 236),
  createTheme('ocean', 'Ocean', 'Deep ocean blues', oklchToVar(0.64, 0.15, 255), oklchToVar(0.54, 0.15, 255), 255),
  createTheme('slate', 'Slate', 'Cool slate blue', oklchToVar(0.58, 0.1, 270), oklchToVar(0.48, 0.1, 270), 270),
  
  // Purples & Violets
  createTheme('indigo', 'Indigo', 'Deep indigo tones', oklchToVar(0.62, 0.18, 264), oklchToVar(0.52, 0.18, 264), 264),
  createTheme('violet', 'Violet', 'Rich violet palette', oklchToVar(0.68, 0.16, 280), oklchToVar(0.58, 0.16, 280), 280),
  createTheme('purple', 'Purple', 'Warm purple palette', oklchToVar(0.7, 0.15, 290), oklchToVar(0.6, 0.15, 290), 290),
  createTheme('plum', 'Plum', 'Sophisticated plum', oklchToVar(0.65, 0.12, 300), oklchToVar(0.55, 0.12, 300), 300),
  
  // Greens & Teals
  createTheme('emerald', 'Emerald', 'Vibrant emerald green', oklchToVar(0.75, 0.16, 150), oklchToVar(0.65, 0.16, 150), 150),
  createTheme('teal', 'Teal', 'Sophisticated teal', oklchToVar(0.74, 0.12, 175), oklchToVar(0.64, 0.12, 175), 175),
  createTheme('forest', 'Forest', 'Deep forest green', oklchToVar(0.58, 0.15, 140), oklchToVar(0.48, 0.15, 140), 140),
  createTheme('mint', 'Mint', 'Fresh mint green', oklchToVar(0.72, 0.1, 165), oklchToVar(0.62, 0.1, 165), 165),
  
  // Reds, Oranges & Ambers
  createTheme('rose', 'Rose', 'Elegant rose palette', oklchToVar(0.72, 0.15, 0), oklchToVar(0.62, 0.15, 0), 0),
  createTheme('orange', 'Orange', 'Warm orange tones', oklchToVar(0.72, 0.18, 45), oklchToVar(0.62, 0.18, 45), 45),
  createTheme('amber', 'Amber', 'Golden amber', oklchToVar(0.72, 0.18, 60), oklchToVar(0.62, 0.18, 60), 60),
  createTheme('scarlet', 'Scarlet', 'Bold scarlet red', oklchToVar(0.65, 0.2, 20), oklchToVar(0.55, 0.2, 20), 20),
  
  // Grays & Neutrals
  createTheme('neutral', 'Neutral', 'Pure neutral gray', oklchToVar(0.68, 0.02, 240), oklchToVar(0.58, 0.02, 240), 240),
  createTheme('stone', 'Stone', 'Warm stone gray', oklchToVar(0.68, 0.02, 70), oklchToVar(0.58, 0.02, 70), 70),
  createTheme('zinc', 'Zinc', 'Cool zinc gray', oklchToVar(0.68, 0.01, 260), oklchToVar(0.58, 0.01, 260), 260),
  
  // Specialized Palettes
  createTheme('nord', 'Nordisk', 'Nordic-inspired cool palette', oklchToVar(0.72, 0.08, 235), oklchToVar(0.52, 0.1, 235), 235),
  createTheme('aurora', 'Aurora', 'Aurora borealis inspired', oklchToVar(0.75, 0.14, 200), oklchToVar(0.55, 0.14, 200), 200),
];

export const getTheme = (themeId: string): Theme | undefined => 
  themes.find(t => t.id === themeId);

export const getThemeNames = (): { id: string; name: string }[] =>
  themes.map(t => ({ id: t.id, name: t.name }));
