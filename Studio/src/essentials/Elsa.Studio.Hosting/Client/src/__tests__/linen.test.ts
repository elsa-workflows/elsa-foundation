import { describe, expect, it } from "vitest";
import { readAppearancePreferences, writeAppearancePreferences } from "../app/themes/appearance";
import { applyWorkspaceStyle } from "../app/themes/workspaceStyles";
import { normalizeThemeStore } from "../app/themes/themeStoreApi";
import { builtInThemeDefinitions } from "../app/themes/presets";
function storage() {
  const values = new Map<string, string>();
  return { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value); }, removeItem: (key: string) => { values.delete(key); } };
}
describe("Linen defaults", () => {
  it("uses Linen for a fresh profile and preserves an explicit preset choice", () => {
    const saved = storage();
    expect(readAppearancePreferences(saved).workspaceStyle).toBe("linen");
    writeAppearancePreferences({ workspaceStyle: null }, saved);
    expect(readAppearancePreferences(saved).workspaceStyle).toBeNull();
  });
  it("adds Linen as the default palette and retains the existing palettes", () => {
    expect(normalizeThemeStore().defaultThemeId).toBe("linen");
    expect(builtInThemeDefinitions.map(theme => theme.id)).toContain("classic");
    expect(builtInThemeDefinitions.map(theme => theme.id)).toEqual(expect.arrayContaining(["linen", "classic", "meridian", "drift", "schematic", "atelier", "elsa-cloud", "signal", "dusk"]));
    expect(builtInThemeDefinitions).toHaveLength(13);
  });
  it("keeps rounded sans geometry across palettes", () => {
    const result = applyWorkspaceStyle(builtInThemeDefinitions[0], "linen");
    expect(result.typography?.sans).toContain("Manrope");
    expect(result.shape?.radiusLg).toBe("20px");
  });
});

describe("host presentation defaults", () => {
  it("uses configured defaults without overriding stored user choices", () => {
    window.__ELSA_STUDIO_RUNTIME__ = { appearance: { workspaceStyle: "focus", paletteId: "drift", mode: "dim", navigation: "top" } };
    try {
      const saved = storage();
      expect(readAppearancePreferences(saved)).toMatchObject({ workspaceStyle: "focus", colorSchemeId: "drift", mode: "dim", navigation: "top" });
      writeAppearancePreferences({ workspaceStyle: "linen", colorSchemeId: "signal", mode: "light", navigation: "left" }, saved);
      expect(readAppearancePreferences(saved)).toMatchObject({ workspaceStyle: "linen", colorSchemeId: "signal", mode: "light", navigation: "left" });
      writeAppearancePreferences({ themeId: "classic", workspaceStyle: null, colorSchemeId: null }, saved);
      expect(readAppearancePreferences(saved)).toMatchObject({ themeId: "classic", workspaceStyle: null, colorSchemeId: null });
    } finally { delete window.__ELSA_STUDIO_RUNTIME__; }
  });
});
