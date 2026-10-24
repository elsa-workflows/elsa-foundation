// @vitest-environment node

import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import ts from "typescript";
import { describe, expect, it } from "vitest";

// Guard: CI must typecheck Studio/tests/live with Node typings. Those journeys are opt-in against
// a live pair, so a missing process global (or a drifted label) never failed the Studio typecheck
// job (#3160). This test fails on main, where no live tsconfig exists and `pnpm typecheck` only
// walks workspace packages.

const liveTsconfigPath = fileURLToPath(new URL("../../../../../../tests/live/tsconfig.json", import.meta.url));
const studioPackagePath = fileURLToPath(new URL("../../../../../../package.json", import.meta.url));

function flatten(message: ts.Diagnostic["messageText"]) {
  return ts.flattenDiagnosticMessageText(message, "\n");
}

function typecheckLiveSpecs() {
  const parsed = ts.getParsedCommandLineOfConfigFile(liveTsconfigPath, {}, {
    ...ts.sys,
    onUnRecoverableConfigFileDiagnostic(diagnostic) {
      throw new Error(flatten(diagnostic.messageText));
    }
  });
  if (!parsed) throw new Error(`TypeScript did not parse ${liveTsconfigPath}`);
  if (parsed.errors.length > 0) {
    throw new Error(parsed.errors.map(diagnostic => flatten(diagnostic.messageText)).join("\n"));
  }

  const program = ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options });
  return {
    fileNames: parsed.fileNames.map(name => name.split("\\").join("/")),
    types: parsed.options.types ?? [],
    diagnostics: ts.getPreEmitDiagnostics(program).map(diagnostic => {
      const file = diagnostic.file;
      const location = file && diagnostic.start !== undefined
        ? `${file.fileName.split("\\").join("/")}:${file.getLineAndCharacterOfPosition(diagnostic.start).line + 1}`
        : "tsconfig";
      return `${location}: ${flatten(diagnostic.messageText)}`;
    })
  };
}

describe("live Playwright specs are typechecked", () => {
  it("covers Studio/tests/live from the Studio typecheck job, with Node process typings", () => {
    expect(existsSync(liveTsconfigPath), "Studio/tests/live/tsconfig.json must exist so CI typechecks the journeys").toBe(true);

    const typecheck = JSON.parse(readFileSync(studioPackagePath, "utf8")).scripts?.typecheck ?? "";
    expect(typecheck, "pnpm typecheck must invoke the live tsconfig, not only workspace packages").toMatch(/tests\/live/);

    const { fileNames, types, diagnostics } = typecheckLiveSpecs();
    expect(types).toContain("node");
    expect(fileNames.some(name => name.endsWith("/tests/live/support/journey.ts"))).toBe(true);
    expect(fileNames.some(name => name.endsWith("/tests/live/compensation-journey.spec.ts"))).toBe(true);
    expect(fileNames.some(name => name.endsWith("/tests/live/playwright.live.config.ts"))).toBe(true);
    expect(diagnostics, diagnostics.join("\n")).toEqual([]);
  });
});
