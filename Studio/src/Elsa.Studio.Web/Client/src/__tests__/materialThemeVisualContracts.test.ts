// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { getTheme } from "../app/themes/presets";

const tokensCss = readFileSync(new URL("../app/ui/tokens.css", import.meta.url), "utf8");
const stylesCss = readFileSync(new URL("../app/styles.css", import.meta.url), "utf8");

const contentSurfaceTokens = [
  "--studio-material-panel-bg",
  "--studio-material-panel-bg-strong",
  "--studio-material-panel-bg-soft",
  "--studio-material-content-bg",
  "--studio-material-canvas-bg",
  "--studio-material-row-bg",
  "--studio-material-well-bg"
] as const;

function themeToken(theme: string, name: string) {
  const blockStart = tokensCss.indexOf(`html[data-theme="${theme}"] {`);
  const blockEnd = tokensCss.indexOf("\n}", blockStart);
  const themeBlock = blockStart >= 0 && blockEnd >= 0 ? tokensCss.slice(blockStart, blockEnd) : "";
  const tokenStart = themeBlock.indexOf(`${name}:`);
  const valueStart = tokenStart + name.length + 1;
  const valueEnd = themeBlock.indexOf(";", valueStart);
  const value = tokenStart >= 0 && valueEnd >= 0 ? themeBlock.slice(valueStart, valueEnd).trim() : "";

  if (!value) {
    throw new Error(`Could not find ${name} in the ${theme} token block`);
  }

  return value;
}

function relativeLuminance(hex: string) {
  const channels = hex.match(/[\da-f]{2}/gi)?.map(channel => Number.parseInt(channel, 16) / 255);
  if (!channels || channels.length !== 3) {
    throw new Error(`Expected a six-digit hex color, received ${hex}`);
  }

  const [red, green, blue] = channels.map(channel =>
    channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  );

  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

function contrastRatio(foreground: string, background: string) {
  const lighter = Math.max(relativeLuminance(foreground), relativeLuminance(background));
  const darker = Math.min(relativeLuminance(foreground), relativeLuminance(background));
  return (lighter + 0.05) / (darker + 0.05);
}

function ruleBody(source: string, selector: string) {
  const selectorStart = source.indexOf(selector);
  const start = source.indexOf("{", selectorStart);
  const end = source.indexOf("\n}", start);
  if (selectorStart < 0 || start < 0 || end < 0) {
    throw new Error(`Could not find the ${selector} rule`);
  }

  return source.slice(start, end);
}

describe("material theme visual contracts", () => {
  it.each(["stone", "brass-instrument"])("keeps %s texture off content-bearing surfaces", theme => {
    for (const surfaceToken of contentSurfaceTokens) {
      expect(themeToken(theme, surfaceToken), surfaceToken).not.toContain("var(--studio-material-texture)");
    }

    expect(themeToken(theme, "--studio-material-body-bg")).toContain("var(--studio-material-texture)");
  });

  it("keeps Brass Instrument secondary text and control edges legible", () => {
    const raisedSurface = themeToken("brass-instrument", "--studio-surface-raised");
    expect(contrastRatio(themeToken("brass-instrument", "--studio-text-muted"), raisedSurface)).toBeGreaterThanOrEqual(7);
    expect(contrastRatio(themeToken("brass-instrument", "--studio-border"), raisedSurface)).toBeGreaterThanOrEqual(3);
    expect(themeToken("brass-instrument", "--studio-material-edge")).toBe("var(--studio-border)");
  });

  it("keeps the Brass Instrument primitive palette aligned with its semantic contrast fix", () => {
    const brass = getTheme("brass-instrument")?.dark;
    const lightness = (value: string | undefined) => Number(value?.match(/^oklch\(([\d.]+)/)?.[1]);

    expect(lightness(brass?.foreground)).toBeGreaterThanOrEqual(0.9);
    expect(lightness(brass?.mutedForeground)).toBeGreaterThanOrEqual(0.78);
    expect(lightness(brass?.border)).toBeGreaterThanOrEqual(0.58);
  });

  it("styles Brass Instrument disabled controls without fading their contrast", () => {
    const disabledRule = ruleBody(
      stylesCss,
      'html[data-theme="brass-instrument"] :is(button, input, select, textarea):disabled'
    );

    expect(disabledRule).toContain("border-color: var(--studio-border)");
    expect(disabledRule).toContain("background: var(--studio-surface-muted)");
    expect(disabledRule).toContain("color: var(--studio-text-muted)");
    expect(disabledRule).toContain("opacity: 1");
  });

  it("keeps Walnut Workshop navigation text legible on hover", () => {
    const hoverRule = ruleBody(
      stylesCss,
      'html[data-theme="walnut-workshop"] .nav-section a:hover'
    );

    expect(hoverRule).toContain("color: var(--studio-accent-text)");
    expect(hoverRule).toContain("text-shadow:");
  });
});
