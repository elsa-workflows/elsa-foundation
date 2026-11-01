import { describe, expect, it } from "vitest";
import { cloneThemeDefinition, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { createCustomThemeFrom, normalizeThemeStore, validateThemeDefinition } from "../app/themes/themeStoreApi";
import { applyWorkspaceStyle, isWorkspaceStyle } from "../app/themes/workspaceStyles";
import { appearanceStorageKeys, readAppearancePreferences } from "../app/themes/appearance";

describe("Sketchbook appearance", () => {
  it("registers a complete four-mode preset and retains its workspace when duplicated", () => {
    const theme = getTheme("builtin:sketchbook");
    expect(theme).toBeDefined();
    expect(theme!.workspaceStyle).toBe("sketchbook");
    expect(validateThemeDefinition(theme!).valid).toBe(true);
    for (const mode of ["light", "dark", "dim", "high-contrast"] as const) expect(getThemeModeDefinition(theme!, mode)).toBeDefined();
    const copy = createCustomThemeFrom(theme!, "acme-drawing", "Acme drawing");
    expect(cloneThemeDefinition(copy).workspaceStyle).toBe("sketchbook");
    expect(normalizeThemeStore({ themes: [JSON.parse(JSON.stringify(copy))] }).themes.find(x => x.id === copy.id)?.workspaceStyle).toBe("sketchbook");
  });
  it("applies the same drawing geometry and typography across independent palettes", () => {
    expect(isWorkspaceStyle("sketchbook")).toBe(true);
    const first = applyWorkspaceStyle(getTheme("classic")!, "sketchbook");
    const second = applyWorkspaceStyle(getTheme("drift")!, "sketchbook");
    expect(first.typography).toEqual(second.typography);
    expect(first.shape).toEqual(second.shape);
    expect(first.typography?.display).toContain("Instrument Serif");
    expect(first.typography?.sans).toContain("Manrope");
    expect(first.modes).toBe(getTheme("classic")!.modes);
  });
  it("restores Sketchbook without changing palette, mode, or navigation", () => {
    const values = new Map([[appearanceStorageKeys.workspaceStyle, "sketchbook"], [appearanceStorageKeys.colorSchemeId, "drift"], [appearanceStorageKeys.mode, "dim"], [appearanceStorageKeys.navigation, "top"]]);
    expect(readAppearancePreferences({ getItem: key => values.get(key) ?? null, setItem: () => {}, removeItem: () => {} })).toMatchObject({ workspaceStyle: "sketchbook", colorSchemeId: "drift", mode: "dim", navigation: "top" });
  });
});
