import { describe, expect, it } from "vitest";
import { cloneThemeDefinition, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { createCustomThemeFrom, getSelectableThemes, normalizeThemeStore, validateThemeDefinition } from "../app/themes/themeStoreApi";
import { minimalStudioPresets } from "../app/themes/minimalPresets";
import { appearanceStorageKeys, readAppearancePreferences } from "../app/themes/appearance";

describe("minimal Studio theme definitions", () => {
  it.each(["porcelain", "atelier-minimal", "graphite"])("preserves an existing custom identity %s in every source mode", id => {
    const custom = { ...createCustomThemeFrom(getTheme("classic")!, id, `Acme ${id}`), enabled: true, published: true };
    expect(validateThemeDefinition(custom).valid).toBe(true);
    for (const themesMode of ["AddToBuiltIns", "DirectoryOnly"] as const) {
      const store = normalizeThemeStore({ themes: [custom], defaultThemeId: id, directoryThemeIds: [id], themesMode });
      expect(store.defaultThemeId).toBe(id);
      expect(getSelectableThemes(store).find(theme => theme.id === id)?.name).toBe(custom.name);
    }
  });
  it.each(minimalStudioPresets)("keeps $name usable in every mode and as a custom copy", preset => {
    const theme = getTheme(preset.id)!;
    expect(theme.workspaceStyle).toBe("minimal");
    expect(validateThemeDefinition(theme).valid).toBe(true);
    for (const mode of ["light", "dark", "dim", "high-contrast"] as const) expect(getThemeModeDefinition(theme, mode)).toBeDefined();
    const copy = createCustomThemeFrom(theme, "my-minimal", "My Studio");
    expect(copy.workspaceStyle).toBe("minimal");
    expect(cloneThemeDefinition(copy).workspaceStyle).toBe("minimal");
    const wire = JSON.parse(JSON.stringify(copy));
    expect(normalizeThemeStore({ themes: [wire] }).themes.find(item => item.id === copy.id)?.workspaceStyle).toBe("minimal");
  });

  it("retains the original Atelier independently of the new concept", () => {
    const original = getTheme("atelier")!;
    expect(original.layout).toBe("editorial");
    expect(original.typography?.display).toContain("Serif");
    expect(original.modes.light.primary).not.toBe(getTheme("builtin:atelier")!.modes.light.primary);
    expect(getTheme("builtin:atelier")!.typography?.display).toBeUndefined();
  });

  it("rejects invalid presentation metadata and safely normalizes an old or invalid wire value", () => {
    const copy = createCustomThemeFrom(getTheme("builtin:porcelain")!, "my-studio", "My Studio");
    const invalid = { ...copy, workspaceStyle: "untrusted-style" } as unknown as typeof copy;
    expect(validateThemeDefinition(invalid).issues.some(issue => issue.path === "workspaceStyle" && issue.severity === "error")).toBe(true);
    expect(normalizeThemeStore({ themes: [invalid] }).themes.find(item => item.id === copy.id)?.workspaceStyle).toBeUndefined();
    expect(normalizeThemeStore({ themes: [{ ...copy, workspaceStyle: undefined }] }).themes.find(item => item.id === copy.id)?.workspaceStyle).toBeUndefined();
  });

  it("accepts a persisted Minimal workspace without changing the other axes", () => {
    const values = new Map([[appearanceStorageKeys.workspaceStyle, "minimal"], [appearanceStorageKeys.colorSchemeId, "builtin:graphite"], [appearanceStorageKeys.mode, "dim"], [appearanceStorageKeys.navigation, "top"]]);
    const preferences = readAppearancePreferences({ getItem: key => values.get(key) ?? null, setItem: () => {}, removeItem: () => {} });
    expect(preferences).toMatchObject({ workspaceStyle: "minimal", colorSchemeId: "builtin:graphite", mode: "dim", navigation: "top" });
  });
});
