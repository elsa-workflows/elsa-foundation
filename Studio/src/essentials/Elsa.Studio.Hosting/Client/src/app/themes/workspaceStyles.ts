import type { StudioThemeLayout } from "../../sdk";
import type { NavMode } from "./navMode";
import type { Theme } from "./presets";

export type WorkspaceStyle = "linen" | "minimal" | "quiet" | "focus" | "open" | "sketchbook";

export const workspaceStyles: readonly {
  id: WorkspaceStyle;
  name: string;
  description: string;
  layout: StudioThemeLayout;
  navigation: NavMode;
}[] = [
  { id: "linen", name: "Linen", description: "Soft, rounded surfaces and a quietly contrasting navigation.", layout: "classic", navigation: "left" },
  { id: "sketchbook", name: "Sketchbook", description: "Fine graphite, paper and watercolor with a handcrafted drawing-room feel.", layout: "classic", navigation: "left" },
  { id: "minimal", name: "Minimal", description: "Neutral surfaces, fine dotted details and softly rounded tools.", layout: "classic", navigation: "left" },
  { id: "quiet", name: "Quiet Studio", description: "A clear, familiar workspace with docked tools.", layout: "classic", navigation: "left" },
  { id: "focus", name: "Focus Canvas", description: "An open canvas with floating tools and compact navigation.", layout: "floating", navigation: "left" },
  { id: "open", name: "Open Studio", description: "A spacious workspace with inline authoring and top navigation.", layout: "editorial", navigation: "top" }
];

export function isWorkspaceStyle(value: unknown): value is WorkspaceStyle {
  return workspaceStyles.some(style => style.id === value);
}

export function getWorkspaceStyle(style: WorkspaceStyle) {
  return workspaceStyles.find(candidate => candidate.id === style)!;
}

/** Style owns type and shape; selecting another palette cannot change either. */
export function applyWorkspaceStyle(theme: Theme, style: WorkspaceStyle | null): Theme {
  if (style === null) return theme;
  if (style === "sketchbook") return {
    ...theme,
    layout: getWorkspaceStyle(style).layout,
    typography: {
      sans: '"Manrope Variable", "Manrope", "Segoe UI", system-ui, sans-serif',
      mono: '"Geist Mono Variable", "SF Mono", Consolas, monospace',
      display: '"Instrument Serif", Georgia, serif'
    },
    shape: { radiusSm: "5px", radius: "8px", radiusMd: "12px", radiusLg: "18px", radiusXl: "24px" }
  };
  return {
    ...theme,
    layout: getWorkspaceStyle(style).layout,
    typography: {
      sans: style === "linen" || style === "minimal"
        ? '"Manrope Variable", "Manrope", "Segoe UI", system-ui, sans-serif'
        : style === "open"
        ? '"Instrument Sans Variable", "Segoe UI", system-ui, sans-serif'
        : '"Geist Variable", "Geist", "Segoe UI", system-ui, sans-serif',
      mono: '"Geist Mono Variable", "SF Mono", Consolas, monospace'
    },
    shape: style === "minimal"
      ? { radiusSm: "6px", radius: "8px", radiusMd: "12px", radiusLg: "16px", radiusXl: "20px" }
      : style === "linen"
      ? { radiusSm: "9px", radius: "11px", radiusMd: "16px", radiusLg: "20px", radiusXl: "24px" }
      : { radiusSm: "5px", radius: "8px", radiusMd: "10px", radiusLg: "12px", radiusXl: "14px" }
  };
}
