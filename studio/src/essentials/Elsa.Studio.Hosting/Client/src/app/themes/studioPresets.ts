import { minimalStudioPresets } from "./minimalPresets";

/** Complete reference combinations. Palette/mode/workspace controls remain independent. */
export const studioPresets = [
  ...minimalStudioPresets,
  { id: "space", name: "Space", description: "A subtle starfield sidebar with a calm Minimal workspace.", mode: "light" }
] as const;

export type StudioPresetId = typeof studioPresets[number]["id"];