import { describe, expect, it } from "vitest";
import { findModuleCssContractViolations, moduleCssContractPlugin } from "../../../../../../vite.module.css-contract";

describe("module CSS contract", () => {
  it("requires flat content-hashed names for cache-safe deferred loading", () => {
    expect(findModuleCssContractViolations(["module-a_B-12.css", "module.js"])).toEqual([]);
    expect(findModuleCssContractViolations(["module.css", "module2.css", "assets/module-hash.css", "Module-HASH.CSS"]))
      .toEqual(["module.css", "module2.css", "assets/module-hash.css", "Module-HASH.CSS"]);
  });

  it.each([{ lib: { entry: "module.tsx" }, cssCodeSplit: true }, { lib: false, cssCodeSplit: false }])(
    "rejects build modes that cannot load deferred styles: %j", build => {
      const handler = moduleCssContractPlugin().configResolved as (config: unknown) => void;
      expect(() => handler({ build })).toThrow(/app-mode ESM/);
    }
  );

  const generate = (bundle: Record<string, unknown>) => {
    const emitted: Array<{ fileName: string; source: string }> = [];
    const context = {
      error(message: string): never { throw new Error(message); },
      emitFile(asset: { fileName: string; source: string }) { emitted.push(asset); }
    };
    const handler = moduleCssContractPlugin().generateBundle as (this: typeof context, options: unknown, bundle: unknown) => void;
    handler.call(context, {}, bundle);
    return emitted;
  };
  const css = (fileName: string) => ({ type: "asset", fileName, source: fileName });
  const chunk = (fileName: string, imports: string[], styles: string[], isEntry = false) => ({
    type: "chunk", fileName, imports, isEntry, viteMetadata: { importedCss: new Set(styles) }
  });

  it("emits only the ordered static closure and preserves legacy aliases for all styles", () => {
    const emitted = generate({
      "module.js": chunk("module.js", ["shared.js"], ["module-entry.css"], true),
      "shared.js": chunk("shared.js", [], ["module-shared.css"]),
      "editor.js": chunk("editor.js", [], ["module-canvas.css"]),
      "module-entry.css": css("module-entry.css"),
      "module-shared.css": css("module-shared.css"),
      "module-canvas.css": css("module-canvas.css")
    });
    expect(JSON.parse(emitted[0].source)).toEqual({ version: 1, styles: ["module-shared.css", "module-entry.css"] });
    expect(emitted.slice(1)).toEqual([
      { type: "asset", fileName: "module.css", source: "module-entry.css" },
      { type: "asset", fileName: "module2.css", source: "module-shared.css" },
      { type: "asset", fileName: "module3.css", source: "module-canvas.css" }
    ]);
  });

  it("emits an explicit empty list for a styleless entry", () => {
    expect(JSON.parse(generate({ "module.js": chunk("module.js", [], [], true) })[0].source)).toEqual({ version: 1, styles: [] });
  });

  it("fails when CSS naming drifts or referenced CSS disappears", () => {
    expect(() => generate({ "styles.css": css("styles.css") })).toThrow(/StudioModuleManifestProvider/);
    expect(() => generate({ "module.js": chunk("module.js", [], ["module-missing.css"], true) })).toThrow(/Missing module stylesheet/);
  });
});
