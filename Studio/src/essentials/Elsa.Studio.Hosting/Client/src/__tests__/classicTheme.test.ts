// @vitest-environment node

import { describe, expect, it } from "vitest";
import { allThemeModes, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { foundationThemeIds } from "../app/themes/foundationThemes";
import { parseOklch, read, ruleDeclarations } from "./themeTestUtils";

const tokensCss = read("../app/ui/tokens.css");
const classic = getTheme("classic")!;

/** WCAG 2 contrast. Strings are `oklch(L C H)`; numbers are already relative luminance. */
function contrast(first: string | number, second: string | number) {
  const [a, b] = [typeof first === "number" ? first : luminance(first), typeof second === "number" ? second : luminance(second)]
    .sort((x, y) => y - x);
  return (a + 0.05) / (b + 0.05);
}

/** Relative luminance of `color-mix(in srgb, first amount, second)`. */
function mixSrgb(first: string, second: string, amount: number) {
  const a = linearToSrgb(oklchToLinear(first));
  const b = linearToSrgb(oklchToLinear(second));
  return luminanceOf({
    r: srgbToLinear(amount * a.r + (1 - amount) * b.r),
    g: srgbToLinear(amount * a.g + (1 - amount) * b.g),
    b: srgbToLinear(amount * a.b + (1 - amount) * b.b)
  });
}

function linearToSrgb(c: { r: number; g: number; b: number }) {
  const enc = (v: number) => (v <= 0.0031308 ? 12.92 * v : 1.055 * v ** (1 / 2.4) - 0.055);
  return { r: enc(c.r), g: enc(c.g), b: enc(c.b) };
}

function srgbToLinear(v: number) {
  return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
}

function oklchToLinear(color: string) {
  const { lightness, chroma, hue } = parseOklch(color);
  const radians = (hue * Math.PI) / 180;
  const okA = chroma * Math.cos(radians);
  const okB = chroma * Math.sin(radians);
  const l = (lightness + 0.3963377774 * okA + 0.2158037573 * okB) ** 3;
  const m = (lightness - 0.1055613458 * okA - 0.0638541728 * okB) ** 3;
  const s = (lightness - 0.0894841775 * okA - 1.291485548 * okB) ** 3;
  const clamp = (value: number) => Math.min(1, Math.max(0, value));
  return {
    r: clamp(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    g: clamp(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    b: clamp(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)
  };
}

function luminanceOf(lin: { r: number; g: number; b: number }) {
  return 0.2126 * lin.r + 0.7152 * lin.g + 0.0722 * lin.b;
}

function luminance(color: string) {
  return luminanceOf(oklchToLinear(color));
}

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

    expect(light.lightness).toBe(0.5);
    expect(light.chroma).toBeGreaterThan(0.12);
    expect(getThemeModeDefinition(classic, "light")!.primaryForeground).toBe("oklch(0.99 0 0)");
  });

  it("keeps Light primary at ≥4.5:1 as text on muted, wash and the pale surfaces", () => {
    const light = getThemeModeDefinition(classic, "light")!;
    const floor = 4.5;

    expect(contrast(light.primaryForeground, light.primary)).toBeGreaterThanOrEqual(floor);
    expect(contrast(light.primary, light.background)).toBeGreaterThanOrEqual(floor);
    expect(contrast(light.primary, light.card)).toBeGreaterThanOrEqual(floor);
    expect(contrast(light.primary, light.sidebar)).toBeGreaterThanOrEqual(floor);
    expect(contrast(light.primary, light.muted)).toBeGreaterThanOrEqual(floor);
    expect(contrast(light.primary, light.accent)).toBeGreaterThanOrEqual(floor);
    // `--studio-accent-soft` is 10% primary over the card. L 0.51 sat at 4.50; L 0.50 leaves margin.
    expect(contrast(light.primary, mixSrgb(light.primary, light.card, 0.1))).toBeGreaterThan(4.6);
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
    const light = ruleDeclarations(tokensCss, 'html:where(:not([data-workspace-style]))[data-theme="classic"]');
    const dark = ruleDeclarations(tokensCss, 'html:where(:not([data-workspace-style]))[data-theme="classic"][data-theme-mode="dark"]');

    for (const recipe of [light, dark]) {
      expect(Object.keys(recipe)).toEqual(expect.arrayContaining(["--shadow-sm", "--shadow-md", "--shadow-lg", "--shadow-xl"]));
    }
    expect(light["--studio-title-weight"]).toBe("600");
  });

  it("does not join the shared dark elevation recipe", () => {
    expect(tokensCss).not.toMatch(/\[data-theme="classic"\]\)\[data-theme-mode="dark"\]/);
  });
});
