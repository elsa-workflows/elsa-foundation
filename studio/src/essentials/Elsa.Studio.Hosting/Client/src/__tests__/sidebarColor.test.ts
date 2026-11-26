import { describe, expect, it } from "vitest";
import { getTheme, getThemeModeDefinition, type Theme, type ThemeMode } from "../app/themes/presets";
import {
  isDarkColor,
  isSidebarColorPreference,
  resolveNavigationColors,
  resolveSidebarColor
} from "../app/themes/sidebarColor";

const theme = (id: string) => getTheme(id)!;
const sidebarPair = (palette: Theme, mode: ThemeMode) => {
  const colors = getThemeModeDefinition(palette, mode)!;
  return { bg: colors.sidebar, text: colors.sidebarForeground, active: colors.sidebarActive, activeText: colors.sidebarActiveForeground };
};

describe("sidebar color", () => {
  it("accepts only the three preferences", () => {
    expect(["theme", "light", "dark"].every(isSidebarColorPreference)).toBe(true);
    expect(isSidebarColorPreference("dim")).toBe(false);
    expect(isSidebarColorPreference(undefined)).toBe(false);
  });

  it("pins the tone except for Theme default and High contrast", () => {
    expect(resolveSidebarColor("dark", "light")).toBe("dark");
    expect(resolveSidebarColor("dark", "dim")).toBe("dark");
    expect(resolveSidebarColor("light", "dark")).toBe("light");
    expect(resolveSidebarColor("theme", "light")).toBeUndefined();
    expect(resolveSidebarColor("dark", "high-contrast")).toBeUndefined();
  });

  it("keeps a dark sidebar in Light and Dim workspaces", () => {
    const signal = theme("signal");
    expect(resolveNavigationColors(signal, "light", "minimal", "dark")).toEqual(sidebarPair(signal, "dark"));
    expect(resolveNavigationColors(signal, "dim", null, "dark")).toEqual(sidebarPair(signal, "dark"));
  });

  it("keeps a light sidebar in a Dark workspace", () => {
    const signal = theme("signal");
    expect(resolveNavigationColors(signal, "dark", "minimal", "light")).toEqual(sidebarPair(signal, "light"));
  });

  it("uses the light card surface when the palette's light mode ships a dark sidebar", () => {
    const linen = theme("linen");
    const light = getThemeModeDefinition(linen, "light")!;
    expect(isDarkColor(light.sidebar)).toBe(true);
    expect(resolveNavigationColors(linen, "dark", "linen", "light")).toEqual({
      bg: light.card, text: light.cardForeground, active: light.muted, activeText: light.foreground
    });
  });

  it("follows the workspace for Theme default, including Linen's dark navigation in Light", () => {
    const signal = theme("signal");
    expect(resolveNavigationColors(signal, "light", "minimal", "theme")).toEqual(sidebarPair(signal, "light"));
    expect(resolveNavigationColors(signal, "light", "linen", "theme")).toEqual(sidebarPair(signal, "dark"));
    expect(resolveNavigationColors(signal, "dark", null, "theme")).toEqual(sidebarPair(signal, "dark"));
  });

  it("keeps High contrast's own navigation whatever the preference", () => {
    const signal = theme("signal");
    for (const preference of ["theme", "light", "dark"] as const) {
      expect(resolveNavigationColors(signal, "high-contrast", "minimal", preference)).toEqual(sidebarPair(signal, "high-contrast"));
    }
  });

  it("reads the lightness of every allowed token color format", () => {
    expect(isDarkColor("oklch(0.26 0.01 85)")).toBe(true);
    expect(isDarkColor("oklch(0.98 0.01 85)")).toBe(false);
    expect(isDarkColor("#111")).toBe(true);
    expect(isDarkColor("#f5f5f5")).toBe(false);
    expect(isDarkColor("rgb(20, 24, 32)")).toBe(true);
    expect(isDarkColor("rgba(250 250 250 / 1)")).toBe(false);
    expect(isDarkColor("hsl(220, 20%, 12%)")).toBe(true);
    expect(isDarkColor("hsl(220 20% 96%)")).toBe(false);
    expect(isDarkColor("var(--sidebar)")).toBeUndefined();
  });
});
