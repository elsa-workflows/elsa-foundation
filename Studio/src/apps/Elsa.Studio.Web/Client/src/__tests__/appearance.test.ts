import { describe, expect, it } from "vitest";
import {
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

  it("reset removes every stored preference and returns the defaults", () => {
    const storage = memoryStorage({ "elsa-studio-theme": "drift", "elsa-studio-theme-mode": "dark", "elsa-studio-nav-mode": "left" });
    expect(resetAppearancePreferences(storage)).toEqual(defaultAppearancePreferences);
    expect(storage.data.size).toBe(0);
  });

  it("does not throw when storage fails", () => {
    const broken = { getItem: () => { throw new Error("denied"); }, setItem: () => { throw new Error("denied"); }, removeItem: () => { throw new Error("denied"); } };
    expect(readAppearancePreferences(broken)).toEqual(defaultAppearancePreferences);
    expect(() => writeAppearancePreferences({ mode: "dark" }, broken)).not.toThrow();
    expect(() => resetAppearancePreferences(broken)).not.toThrow();
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
