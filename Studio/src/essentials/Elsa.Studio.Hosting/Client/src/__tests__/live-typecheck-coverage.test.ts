// @vitest-environment node

import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

// Guard: CI must typecheck Studio/tests/live (Node typings) and Studio/tests/browser.
// Those journeys are opt-in / fixture-hosted, so a missing process global or a drifted
// browser spec never failed the Studio typecheck job (#3160). Diagnostics stay on
// `pnpm typecheck`; this test only asserts the wiring.

const studioRoot = new URL("../../../../../../", import.meta.url);
const liveTsconfigPath = fileURLToPath(new URL("tests/live/tsconfig.json", studioRoot));
const browserTsconfigPath = fileURLToPath(new URL("tests/browser/tsconfig.json", studioRoot));
const studioPackagePath = fileURLToPath(new URL("package.json", studioRoot));

function readJsonc(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(path, "utf8").replace(/^\s*\/\/.*$/gm, ""));
}

describe("Playwright specs are typechecked", () => {
  it("covers Studio/tests/live and Studio/tests/browser from the Studio typecheck job", () => {
    expect(existsSync(liveTsconfigPath), "Studio/tests/live/tsconfig.json must exist so CI typechecks the journeys").toBe(true);
    expect(existsSync(browserTsconfigPath), "Studio/tests/browser/tsconfig.json must exist so CI typechecks the browser specs").toBe(true);

    const typecheck = JSON.parse(readFileSync(studioPackagePath, "utf8")).scripts?.typecheck ?? "";
    expect(typecheck, "pnpm typecheck must invoke the live tsconfig, not only workspace packages").toMatch(/tests\/live\/tsconfig\.json/);
    expect(typecheck, "pnpm typecheck must invoke the browser tsconfig, not only workspace packages").toMatch(/tests\/browser\/tsconfig\.json/);

    const live = readJsonc(liveTsconfigPath);
    const liveOptions = (live.compilerOptions ?? {}) as Record<string, unknown>;
    const liveInclude = (live.include ?? []) as string[];
    expect(liveOptions.types).toEqual(expect.arrayContaining(["node"]));
    expect(liveInclude.some(entry => entry.includes("support"))).toBe(true);
    expect(liveInclude.some(entry => entry.includes("./*.ts") || entry === "./*.ts")).toBe(true);

    const browser = readJsonc(browserTsconfigPath);
    const browserOptions = (browser.compilerOptions ?? {}) as Record<string, unknown>;
    const browserInclude = (browser.include ?? []) as string[];
    expect(browserOptions.types).toEqual([]);
    expect(browserInclude).toEqual(expect.arrayContaining(["./**/*.spec.ts"]));
  });
});
