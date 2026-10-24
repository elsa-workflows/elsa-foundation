// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { failOnDeclarationDiagnostics } from "../../libDeclarationDiagnostics";

describe("failOnDeclarationDiagnostics", () => {
  it("fails the library declaration gate on a TS2742 portability error", () => {
    expect(() => failOnDeclarationDiagnostics([{
      category: 1,
      code: 2742,
      messageText: "The inferred type of 'useWorkflowCanvas' cannot be named without a reference to '.pnpm/@xyflow+system@0.0.77/node_modules/@xyflow/system'."
    }])).toThrow(/Declaration generation failed with 1 error\(s\)\.[\s\S]*TS2742/);
  });

  it("fails when a nested diagnostic message reports a declaration error", () => {
    expect(() => failOnDeclarationDiagnostics([{
      code: 2742,
      messageText: { messageText: "The inferred type of 'useGraphCanvasInteractions' cannot be named without a reference to '@xyflow/system'." }
    }])).toThrow(/TS2742: The inferred type of 'useGraphCanvasInteractions'/);
  });

  it("ignores warning-category diagnostics", () => {
    expect(() => failOnDeclarationDiagnostics([{
      category: 0,
      code: 6133,
      messageText: "'unused' is declared but its value is never read."
    }])).not.toThrow();
  });

  it("passes when declaration emit is clean", () => {
    expect(() => failOnDeclarationDiagnostics([])).not.toThrow();
  });
});

describe("library declaration emit gate", () => {
  it("is wired into the Workflows library build", () => {
    const config = readFileSync(new URL("../../vite.lib.config.ts", import.meta.url), "utf8");
    expect(config).toContain("afterDiagnostic: failOnDeclarationDiagnostics");
  });
});
