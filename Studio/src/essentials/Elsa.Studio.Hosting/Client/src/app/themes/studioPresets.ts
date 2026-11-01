import { minimalStudioPresets } from "./minimalPresets";

/** Complete starting points; later workspace, palette, mode and navigation choices stay independent. */
export const studioPresets = [
  ...minimalStudioPresets.map(preset => ({ ...preset, workspaceStyle: "minimal" as const })),
  { id: "builtin:sketchbook", name: "Sketchbook", description: "Drawing paper, fine graphite and restrained watercolor.", mode: "light", workspaceStyle: "sketchbook" }
] as const;

export type StudioPresetId = typeof studioPresets[number]["id"];
