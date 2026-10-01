// @vitest-environment node

import { describe, expect, it } from "vitest";
import { allThemeModes, getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { parseOklch, read } from "./themeTestUtils";

const tokensCss = read("../app/ui/tokens.css");
const elsaCloudCss = read("../app/themes/elsaCloud.css");
const stylesCss = read("../app/styles.css");

const cloud = getTheme("elsa-cloud")!;

describe("Elsa Cloud palette", () => {
  it("is a near-black, neutral dark ground with cards one step lighter", () => {
    const dark = getThemeModeDefinition(cloud, "dark")!;
    const background = parseOklch(dark.background);
    const card = parseOklch(dark.card);

    expect(background.lightness).toBeLessThanOrEqual(0.16);
    expect(background.chroma).toBeLessThanOrEqual(0.01);
    expect(card.lightness).toBeGreaterThan(background.lightness);
    expect(card.lightness - background.lightness).toBeLessThan(0.08);
  });

  it("keeps one pink hue across every mode, with white text on the Dark and Light buttons", () => {
    const hues = allThemeModes.map(mode => parseOklch(getThemeModeDefinition(cloud, mode)!.primary).hue);

    expect(new Set(hues)).toEqual(new Set([4]));
    for (const mode of ["light", "dark"] as const) {
      expect(getThemeModeDefinition(cloud, mode)!.primaryForeground).toBe("oklch(0.99 0 0)");
    }
  });

  it("lifts Dim's pink above its cards instead of relying on white button text", () => {
    const dim = getThemeModeDefinition(cloud, "dim")!;

    expect(parseOklch(dim.primary).lightness).toBeGreaterThan(parseOklch(dim.card).lightness + 0.4);
    expect(parseOklch(dim.primaryForeground).lightness).toBeLessThan(0.3);
  });

  it("uses mint, not the shared leaf green, for success in every mode", () => {
    for (const mode of allThemeModes) {
      expect(parseOklch(getThemeModeDefinition(cloud, mode)!.success).hue, mode).toBe(160);
    }
  });

  it("follows the console's shape and typography", () => {
    expect(cloud.shape?.radius).toBe("8px");
    expect(cloud.shape?.radiusLg).toBe("12px");
    expect(cloud.typography?.mono).toContain("Geist Mono");
    expect(cloud.layout).toBe("classic");
    expect(getTheme("meridian")!.typography).toBe(cloud.typography);
  });
});

/** The rules of a stylesheet that has no at-rules or nesting: selector list -> declarations. */
function parseRules(css: string) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, "");
  expect(source, "at-rules would need a real parser").not.toContain("@");

  return source.split("}").filter(chunk => chunk.includes("{")).map(chunk => {
    const [selectorList, body] = chunk.split("{");
    const declarations = new Map(body.split(";").map(line => line.split(/:(.*)/s).map(part => part?.trim())).filter(([name]) => name).map(([name, value]) => [name, value] as const));
    return { selectors: selectorList.split(",").map(selector => selector.trim()), declarations };
  });
}

const cloudRules = parseRules(elsaCloudCss);
const scope = 'html[data-theme="elsa-cloud"]';
const ruleFor = (selector: string) => cloudRules.find(rule => rule.selectors.includes(`${scope} ${selector}`));

describe("Elsa Cloud chrome", () => {
  it("is imported by the host stylesheet", () => {
    expect(stylesCss).toContain('@import "./themes/elsaCloud.css";');
  });

  it("scopes every selector to the theme, so no other theme picks up its chrome", () => {
    const selectors = cloudRules.flatMap(rule => rule.selectors);

    expect(selectors.length).toBeGreaterThan(0);
    expect(selectors.filter(selector => !selector.startsWith(`${scope} `))).toEqual([]);
  });

  it.each([".breadcrumb", ".nav-heading", ".studio-status-chip", ".studio-status-pill"])(
    "sets %s in the mono face",
    selector => {
      expect(ruleFor(selector)?.declarations.get("font-family")).toBe("var(--font-mono)");
    });

  it("draws the active navigation item as a bordered box without the accent bar", () => {
    expect(ruleFor(".nav-section a.active")?.declarations.get("box-shadow")).toMatch(/^inset 0 0 0 1px var\(--border\)$/);
    expect(ruleFor(".nav-section a.active::before")?.declarations.get("display")).toBe("none");
  });
});

describe("Elsa Cloud elevation and titles", () => {
  const block = (selector: string) => tokensCss.slice(tokensCss.indexOf(`${selector} {`)).split("}")[0];

  it("has its own subtle dark recipe instead of the shared heavy one", () => {
    const shared = tokensCss.match(/html:is\(([^)]*)\)\[data-theme-mode="dark"\] \{/)![1];

    expect(shared).not.toContain("elsa-cloud");
    expect(block('html[data-theme="elsa-cloud"][data-theme-mode="dark"]')).toContain("--shadow-lg:");
  });

  it("outranks every per-theme dark block under High contrast, whatever the source order", () => {
    // Specificity, not position: `[data-theme]` and `[data-theme-mode]` lift High contrast above the
    // `html[data-theme="…"][data-theme-mode="dark"]` blocks it also matches.
    expect(tokensCss).toContain('html[data-theme][data-theme-mode][data-theme-appearance="high-contrast"] {');
  });

  it("sets titles at a medium weight rather than the shared bold", () => {
    expect(block('html[data-theme="elsa-cloud"]')).toContain("--studio-title-weight: 600;");
  });
});
