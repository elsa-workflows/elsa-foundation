/** Complete reference combinations. Palette/mode/workspace controls remain independent. */
export const minimalStudioPresets = [
  { id: "builtin:porcelain", name: "Porcelain", description: "Light stone navigation and warm white surfaces, with a quiet slate accent.", mode: "light" },
  { id: "builtin:atelier", name: "Atelier", description: "Graphite navigation, a luminous workspace and small sage details.", mode: "light" },
  { id: "builtin:graphite", name: "Graphite", description: "Soft charcoal layers and mist-sage details for a calm dark workspace.", mode: "dark" }
] as const;

export type MinimalStudioPresetId = typeof minimalStudioPresets[number]["id"];
