// @vitest-environment node

import { describe, expect, it } from "vitest";
import { allThemeModes, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { parseOklch, read, ruleDeclarations } from "./themeTestUtils";

const tokensCss = read("../app/ui/tokens.css");
const signalCss = read("../app/themes/signal.css");
const stylesCss = read("../app/styles.css");

const signal = getTheme("signal")!;
const dusk = getTheme("dusk")!;

describe("Signal", () => {
  it("keeps one lime hue family: the accent hue is 125 in Dark, Dim and High contrast", () => {
    for (const mode of ["dark", "dim", "high-contrast"] as const) {
      expect(parseOklch(getThemeModeDefinition(signal, mode)!.primary).hue, mode).toBe(125);
    }
  });

  it("is an instrument panel: a bright lime accent on Dark, tight corners and a mono display face", () => {
    expect(parseOklch(getThemeModeDefinition(signal, "dark")!.primary).lightness).toBeGreaterThan(0.8);
    expect(parseInt(signal.shape!.radius!, 10)).toBeLessThanOrEqual(3);
    expect(signal.typography?.display).toContain("JetBrains Mono");
    expect(signal.layout).toBe("classic");
  });

  it("scopes every stylesheet rule to the theme and is imported by the app stylesheet", () => {
    const selectors = [...signalCss.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/([^{}]+)\{/g)].flatMap(match => match[1].split(",").map(part => part.trim()))
      .filter(Boolean);

    expect(selectors.length).toBeGreaterThan(0);
    expect(selectors.every(selector => selector.startsWith('html[data-theme="signal"]'))).toBe(true);
    expect(stylesCss).toContain('@import "./themes/signal.css";');
  });

  it("declares its own elevation recipes for light and dark, with a semibold title weight", () => {
    const light = ruleDeclarations(tokensCss, 'html[data-theme="signal"]');
    const dark = ruleDeclarations(tokensCss, 'html[data-theme="signal"][data-theme-mode="dark"]');

    for (const recipe of [light, dark]) {
      expect(Object.keys(recipe)).toEqual(expect.arrayContaining(["--shadow-sm", "--shadow-md", "--shadow-lg", "--shadow-xl"]));
    }
    expect(light["--studio-title-weight"]).toBe("600");
  });

  it("is denser than the default: every type-size token steps down from the shared defaults", () => {
    const light = ruleDeclarations(tokensCss, 'html[data-theme="signal"]');
    const defaults = ruleDeclarations(tokensCss, ":root");

    for (const token of ["--studio-page-title-size", "--studio-section-title-size", "--studio-body-size", "--studio-label-size"]) {
      expect(parseFloat(light[token]), token).toBeLessThan(parseFloat(defaults[token]));
    }
  });
});

describe("Dusk", () => {
  it("is dim-first: Dim carries the amber accent while Light and Dark keep the violet", () => {
    expect(parseOklch(getThemeModeDefinition(dusk, "light")!.primary).hue).toBeGreaterThan(280);
    expect(parseOklch(getThemeModeDefinition(dusk, "dark")!.primary).hue).toBeGreaterThan(280);
    expect(parseOklch(getThemeModeDefinition(dusk, "dim")!.primary).hue).toBe(80);
  });

  it("fills the active navigation item with the amber accent in Dim", () => {
    const dim = getThemeModeDefinition(dusk, "dim")!;

    expect(dim.sidebarActive).toBe(dim.primary);
  });

  it("lifts Dim's ground above Dark's, with a violet cast", () => {
    const dark = parseOklch(getThemeModeDefinition(dusk, "dark")!.background);
    const dim = parseOklch(getThemeModeDefinition(dusk, "dim")!.background);

    expect(dim.lightness).toBeGreaterThan(dark.lightness + 0.08);
    expect(dim.chroma).toBeGreaterThan(0.03);
    expect(allThemeModes.every(mode => getThemeModeDefinition(dusk, mode))).toBe(true);
  });

  it("joins the shared dark elevation recipe", () => {
    expect(tokensCss).toMatch(/\[data-theme="dusk"\]\)\[data-theme-mode="dark"\] \{/);
  });
});
