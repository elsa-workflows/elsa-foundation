import { describe, expect, it } from "vitest";
import {
  appearanceChromeStorageKeys,
  appearanceResetStorageKeys,
  appearanceStorageKeys,
  defaultAppearancePreferences,
  getSystemThemeMode,
  isThemeModePreference,
  readAppearancePreferences,
  resetAppearancePreferences,
  resolveModePreference,
  writeAppearancePreferences
} from "../app/themes/appearance";

function memoryStorage(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial));
  return {
    data,
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => void data.set(key, value),
    removeItem: (key: string) => void data.delete(key)
  };
}

const media = (matches: Record<string, boolean>) => (query: string) => ({ matches: matches[query] ?? false });

describe("appearance model", () => {
  it("defaults to the configured theme, system mode and theme navigation", () => {
    expect(readAppearancePreferences(memoryStorage())).toEqual(defaultAppearancePreferences);
    expect(defaultAppearancePreferences).toEqual({ themeId: null, mode: "system", navigation: "theme" });
  });

  it("reads the existing storage keys so earlier choices survive", () => {
    const storage = memoryStorage({ "elsa-studio-theme": "drift", "elsa-studio-theme-mode": "dim", "elsa-studio-nav-mode": "top" });
    expect(readAppearancePreferences(storage)).toEqual({ themeId: "drift", mode: "dim", navigation: "top" });
  });

  it("ignores unknown stored values", () => {
    const storage = memoryStorage({ "elsa-studio-theme-mode": "sepia", "elsa-studio-nav-mode": "bottom" });
    expect(readAppearancePreferences(storage)).toMatchObject({ mode: "system", navigation: "theme" });
  });

  it("writes only the given preferences and clears a null theme", () => {
    const storage = memoryStorage({ "elsa-studio-theme": "drift" });
    writeAppearancePreferences({ mode: "system" }, storage);
    expect(storage.data.get(appearanceStorageKeys.mode)).toBe("system");
    expect(storage.data.get(appearanceStorageKeys.themeId)).toBe("drift");
    writeAppearancePreferences({ themeId: null }, storage);
    expect(storage.data.has(appearanceStorageKeys.themeId)).toBe(false);
  });

  it("reset removes every listed appearance and chrome key and returns the defaults", () => {
    expect(appearanceResetStorageKeys).toHaveLength(8);
    expect([...appearanceResetStorageKeys]).toEqual([
      appearanceStorageKeys.themeId,
      appearanceStorageKeys.mode,
      appearanceStorageKeys.navigation,
      appearanceChromeStorageKeys.sidebarCollapsed,
      appearanceChromeStorageKeys.bottomPanelCollapsed,
      appearanceChromeStorageKeys.bottomPanelHeight,
      appearanceChromeStorageKeys.bottomPanelMaximized,
      appearanceChromeStorageKeys.activeBottomPanel
    ]);
    const storage = memoryStorage({
      "elsa-studio-theme": "drift",
      "elsa-studio-theme-mode": "dark",
      "elsa-studio-nav-mode": "left",
      "elsa-studio-sidebar-collapsed": "true",
      "elsa-studio-bottom-panel-collapsed": "false",
      "elsa-studio-bottom-panel-height": "320",
      "elsa-studio-bottom-panel-maximized": "true",
      "elsa-studio-active-bottom-panel": "console",
      "elsa-studio-unrelated": "keep"
    });
    expect(resetAppearancePreferences(storage)).toEqual(defaultAppearancePreferences);
    expect([...storage.data.keys()]).toEqual(["elsa-studio-unrelated"]);
  });

  it("does not throw when storage fails", () => {
    const broken = { getItem: () => { throw new Error("denied"); }, setItem: () => { throw new Error("denied"); }, removeItem: () => { throw new Error("denied"); } };
    expect(readAppearancePreferences(broken)).toEqual(defaultAppearancePreferences);
    expect(() => writeAppearancePreferences({ mode: "dark" }, broken)).not.toThrow();
    expect(() => resetAppearancePreferences(broken)).not.toThrow();
  });

  it("still clears later reset keys when one removeItem throws", () => {
    const storage = memoryStorage({
      "elsa-studio-theme": "drift",
      "elsa-studio-theme-mode": "dark",
      "elsa-studio-nav-mode": "left",
      "elsa-studio-sidebar-collapsed": "true",
      "elsa-studio-bottom-panel-collapsed": "false",
      "elsa-studio-bottom-panel-height": "320",
      "elsa-studio-bottom-panel-maximized": "true",
      "elsa-studio-active-bottom-panel": "console"
    });
    const originalRemove = storage.removeItem.bind(storage);
    storage.removeItem = (key: string) => {
      if (key === "elsa-studio-theme") throw new Error("denied");
      originalRemove(key);
    };
    expect(() => resetAppearancePreferences(storage)).not.toThrow();
    expect(storage.data.get("elsa-studio-theme")).toBe("drift");
    expect(storage.data.has("elsa-studio-theme-mode")).toBe(false);
    expect(storage.data.has("elsa-studio-active-bottom-panel")).toBe(false);
  });

  it("resolves system mode from the OS, high contrast first", () => {
    expect(getSystemThemeMode(media({}))).toBe("light");
    expect(getSystemThemeMode(media({ "(prefers-color-scheme: dark)": true }))).toBe("dark");
    expect(getSystemThemeMode(media({ "(prefers-color-scheme: dark)": true, "(prefers-contrast: more)": true }))).toBe("high-contrast");
    expect(resolveModePreference("system", media({ "(prefers-color-scheme: dark)": true }))).toBe("dark");
    expect(resolveModePreference("dim", media({ "(prefers-color-scheme: dark)": true }))).toBe("dim");
  });

  it("recognises mode preferences", () => {
    expect(isThemeModePreference("system")).toBe(true);
    expect(isThemeModePreference("high-contrast")).toBe(true);
    expect(isThemeModePreference("auto")).toBe(false);
  });
});
