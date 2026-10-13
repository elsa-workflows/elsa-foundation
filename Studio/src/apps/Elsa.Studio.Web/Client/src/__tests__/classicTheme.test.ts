// @vitest-environment node

import { describe, expect, it } from "vitest";
import { allThemeModes, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { foundationThemeIds } from "../app/themes/foundationThemes";
import { parseOklch, read, ruleDeclarations } from "./themeTestUtils";

const tokensCss = read("../app/ui/tokens.css");
const classic = getTheme("classic")!;

describe("Classic palette", () => {
  it("is the out-of-box default and keeps Meridian selectable", () => {
    expect(foundationThemeIds[0]).toBe("classic");
    expect(foundationThemeIds).toContain("meridian");
    expect(getTheme("meridian")).toBeDefined();
  });

  it("keeps the Elsa 3 sky hue across Light, Dark and Dim", () => {
    for (const mode of ["light", "dark", "dim"] as const) {
      expect(parseOklch(getThemeModeDefinition(classic, mode)!.primary).hue, mode).toBe(237);
    }
  });

  it("darkens Light's sky fill so white button text meets AA, matching Elsa 3's identity", () => {
    const light = parseOklch(getThemeModeDefinition(classic, "light")!.primary);

    expect(light.lightness).toBeLessThan(0.6);
    expect(light.chroma).toBeGreaterThan(0.12);
    expect(getThemeModeDefinition(classic, "light")!.primaryForeground).toBe("oklch(0.99 0 0)");
  });

  it("is a white page with a pale slate sidebar and cards one step off white", () => {
    const light = getThemeModeDefinition(classic, "light")!;
    const background = parseOklch(light.background);
    const sidebar = parseOklch(light.sidebar);
    const card = parseOklch(light.card);

    expect(background.lightness).toBe(1);
    expect(background.chroma).toBe(0);
    expect(sidebar.lightness).toBeGreaterThan(0.97);
    expect(card.lightness).toBeGreaterThan(0.97);
    expect(card.lightness).toBeLessThan(background.lightness);
  });

  it("uses Geist and Elsa 3's 8px radius on the classic layout", () => {
    expect(classic.typography?.sans).toContain("Geist");
    expect(classic.typography?.mono).toContain("Geist Mono");
    expect(classic.shape?.radius).toBe("8px");
    expect(classic.layout).toBe("classic");
    expect(allThemeModes.every(mode => getThemeModeDefinition(classic, mode))).toBe(true);
  });

  it("uses emerald, not the shared leaf green, for success in every mode", () => {
    for (const mode of allThemeModes) {
      expect(parseOklch(getThemeModeDefinition(classic, mode)!.success).hue, mode).toBe(163);
    }
  });
});

describe("Classic chrome", () => {
  it("declares its own elevation recipes for light and dark, with a semibold title weight", () => {
    const light = ruleDeclarations(tokensCss, 'html[data-theme="classic"]');
    const dark = ruleDeclarations(tokensCss, 'html[data-theme="classic"][data-theme-mode="dark"]');

    for (const recipe of [light, dark]) {
      expect(Object.keys(recipe)).toEqual(expect.arrayContaining(["--shadow-sm", "--shadow-md", "--shadow-lg", "--shadow-xl"]));
    }
    expect(light["--studio-title-weight"]).toBe("600");
  });

  it("does not join the shared dark elevation recipe", () => {
    expect(tokensCss).not.toMatch(/\[data-theme="classic"\]\)\[data-theme-mode="dark"\]/);
  });
});
