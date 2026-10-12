// @vitest-environment node

import { readdirSync, readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const srcDir = new URL("../", import.meta.url);
const read = (url: URL) => readFileSync(url, "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const declarations = (css: string) => [...css.matchAll(/(--[\w-]+)\s*:([^;{}]+);/g)].map(([, name, value]) => ({ name, value }));

/** Every call to a function whose name matches `fn`, with balanced parentheses. */
function calls(css: string, fn: RegExp) {
  const found: string[] = [];
  for (const match of css.matchAll(new RegExp(`(?:${fn.source})\\(`, "g"))) {
    let depth = 0;
    let end = match.index;
    for (; end < css.length; end++) {
      if (css[end] === "(") depth++;
      else if (css[end] === ")" && --depth === 0) break;
    }
    found.push(css.slice(match.index, end + 1));
  }
  return found;
}

// Material surface recipes (`--studio-material-*-bg`, `-bg-strong`, `-bg-soft`) are textured,
// layered stacks by contract, even where the host's neutral fallback is a flat colour: a material
// theme supplies its own recipe for any rung it restyles. Any token that is a gradient or image
// today counts too.
const hostTokens = declarations(read(new URL("../../../../../apps/Elsa.Studio.Web/Client/src/app/ui/tokens.css", import.meta.url)))
  .filter(({ name }) => name.startsWith("--studio-material-"));
const imageTokens = new Set(hostTokens
  .filter(({ name, value }) => /-bg(-strong|-soft)?$/.test(name) || /gradient\(|url\(/.test(value))
  .map(({ name }) => name));

const stylesheets = readdirSync(srcDir, { recursive: true })
  .filter(file => file.endsWith(".css"))
  .map(file => ({ file, css: read(new URL(file, srcDir)) }));
const styles = stylesheets.find(({ file }) => file === "styles.css")!.css;

// The material remap of the module's own --wf-* / --xy-* tokens.
const materialScopes = [...styles.matchAll(/html\[data-theme-material\] \.wf-page,[^{]*\{([^}]*)\}/g)]
  .map(([, body]) => declarations(body));
for (const { name, value } of materialScopes.flat()) {
  if (/gradient\(|url\(/.test(value) || [...imageTokens].some(token => value.trim() === `var(${token})`)) imageTokens.add(name);
}

const usesImageToken = (css: string) => [...imageTokens].some(token => css.includes(`var(${token})`));

describe("material theme surface contracts", () => {
  it("knows which module surfaces can become gradient stacks", () => {
    expect([...imageTokens]).toEqual(expect.arrayContaining([
      "--studio-material-panel-bg", "--studio-material-panel-bg-soft", "--studio-material-row-bg",
      "--wf-panel", "--wf-panel-muted", "--wf-surface", "--wf-row"
    ]));
  });

  it("never passes a gradient-capable surface to color-mix() or a gradient colour stop", () => {
    // color-mix() and gradient stops only accept colours: a gradient argument invalidates the
    // whole declaration at computed-value time and the element loses its background entirely.
    const offenders = stylesheets.flatMap(({ file, css }) =>
      calls(css, /color-mix|(?:repeating-)?(?:linear|radial|conic)-gradient/)
        .filter(usesImageToken)
        .map(call => `${file}: ${call.replace(/\s+/g, " ")}`));

    expect(offenders).toEqual([]);
  });

  it("never paints a gradient-capable surface into a colour-only property", () => {
    const colourProperty = /^(?:color|background-color|border(?:-[\w-]+)?|outline(?:-color)?|box-shadow|text-shadow|fill|stroke|caret-color|accent-color|column-rule(?:-color)?)$/;
    const offenders = stylesheets.flatMap(({ file, css }) =>
      [...css.matchAll(/([\w-]+)\s*:([^;{}]+);/g)]
        .filter(([, property, value]) => colourProperty.test(property) && usesImageToken(value))
        .map(([declaration]) => `${file}: ${declaration.replace(/\s+/g, " ")}`));

    expect(offenders).toEqual([]);
  });

  it("gives every gradient-capable panel tier a flat colour tone in the material scope", () => {
    expect(materialScopes).toHaveLength(1);
    const [scope] = materialScopes;
    const value = (name: string) => scope.find(declaration => declaration.name === name)?.value.trim();
    for (const tier of ["--wf-panel", "--wf-panel-muted"]) {
      const tone = value(`${tier}-tone`);
      expect(tone, `${tier}-tone`).toBeDefined();
      expect(usesImageToken(tone!), `${tier}-tone: ${tone}`).toBe(false);
    }
  });
});
