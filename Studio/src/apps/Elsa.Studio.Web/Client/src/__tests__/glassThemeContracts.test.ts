// @vitest-environment node

import { readdirSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { read } from "./themeTestUtils";

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "");

/** Every `color-mix(…)` call in `css`, with balanced parentheses. */
function colorMixCalls(css: string) {
  const calls: string[] = [];
  for (let start = css.indexOf("color-mix("); start >= 0; start = css.indexOf("color-mix(", start + 1)) {
    let depth = 0;
    let end = start;
    for (; end < css.length; end++) {
      if (css[end] === "(") depth++;
      else if (css[end] === ")" && --depth === 0) break;
    }
    calls.push(css.slice(start, end + 1));
  }
  return calls;
}

describe("black-glass theme contracts", () => {
  it("never passes a gradient-valued glass token to color-mix()", () => {
    // color-mix() only accepts colours: a gradient argument invalidates the whole declaration.
    const imageTokens = [...stripComments(read("../app/ui/tokens.css")).matchAll(/(--studio-glass-[\w-]+):([^;]+);/g)]
      .filter(([, , value]) => /gradient\(|url\(|var\(--studio-glass-sheen\)/.test(value))
      .map(([, name]) => name);
    expect(imageTokens).toEqual(expect.arrayContaining(["--studio-glass-bg", "--studio-glass-bg-strong", "--studio-glass-bg-soft"]));

    const appDir = new URL("../app/", import.meta.url);
    const offenders = (readdirSync(appDir, { recursive: true }) as string[])
      .filter(file => file.endsWith(".css"))
      .flatMap(file => colorMixCalls(stripComments(read(`../app/${file}`)))
        .filter(call => imageTokens.some(token => call.includes(`var(${token})`)))
        .map(call => `${file}: ${call}`));

    expect(offenders).toEqual([]);
  });
});
