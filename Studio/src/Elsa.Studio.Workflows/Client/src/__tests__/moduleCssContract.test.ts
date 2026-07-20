import { describe, expect, it } from "vitest";
import {
  findModuleCssContractViolations,
  moduleCssContractPlugin
} from "../../../../../vite.module.css-contract";

describe("findModuleCssContractViolations", () => {
  it.each<[string, string[]]>([
    ["an empty bundle", []],
    ["a single aggregated stylesheet", ["module.css"]],
    ["cssCodeSplit collision-suffixed stylesheets", ["module.css", "module2.css", "module10.css"]],
    ["non-CSS files", ["module.js", "WorkflowDefinitions-C3PwFkNb.js", ".vite/manifest.json"]]
  ])("accepts %s", (_label, fileNames) => {
    expect(findModuleCssContractViolations(fileNames)).toEqual([]);
  });

  it.each([
    "module-C3PwFkNb.css",
    "assets/module.css",
    "module-1.css",
    "styles.css",
    "Module.CSS",
    "module1.css"
  ])("rejects %s", fileName => {
    expect(findModuleCssContractViolations([fileName])).toEqual([fileName]);
  });

  it("returns only the violating files from a mixed bundle", () => {
    expect(
      findModuleCssContractViolations(["module.js", "module.css", "module2.css", "assets/pages.css"])
    ).toEqual(["assets/pages.css"]);
  });
});

describe("moduleCssContractPlugin", () => {
  const plugin = moduleCssContractPlugin();

  // vite:css-post emits the CSS assets in its own generateBundle; without these two flags the
  // guard would run before any CSS exists (or during vitest/dev) and pass vacuously.
  it("runs at build time after vite:css-post", () => {
    expect(plugin.apply).toBe("build");
    expect(plugin.enforce).toBe("post");
  });

  const runGenerateBundle = (fileNames: string[]) => {
    const context = {
      error(message: string): never {
        throw new Error(message);
      }
    };
    const bundle = Object.fromEntries(fileNames.map(name => [name, {}]));
    const handler = plugin.generateBundle as (this: typeof context, options: unknown, bundle: unknown) => void;
    handler.call(context, {}, bundle);
  };

  it("fails the build when a stylesheet escapes the host's naming pattern", () => {
    expect(() => runGenerateBundle(["module.js", "assets/module-C3PwFkNb.css"])).toThrow(
      /StudioModuleManifestProvider/
    );
  });

  it("passes a conforming bundle", () => {
    expect(() => runGenerateBundle(["module.js", "module.css", "module2.css"])).not.toThrow();
  });
});
