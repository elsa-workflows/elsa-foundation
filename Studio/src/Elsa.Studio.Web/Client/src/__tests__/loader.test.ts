import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext } from "../sdk";
import { addModuleVersion, isVersionRangeCompatible, loadStudioModules } from "../app/loader";

describe("studio module loader", () => {
  it("accepts exact, wildcard, and same-major caret ranges", () => {
    expect(isVersionRangeCompatible("1.0.0", "1.0.0")).toBe(true);
    expect(isVersionRangeCompatible("*", "1.0.0")).toBe(true);
    expect(isVersionRangeCompatible("^1.0.0", "1.2.3")).toBe(true);
    expect(isVersionRangeCompatible("^2.0.0", "1.2.3")).toBe(false);
  });

  it("loads compatible modules and records failed imports without stopping later modules", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    await loadStudioModules(
      [
        manifest("first", "/first.js"),
        manifest("bad", "/bad.js"),
        manifest("second", "/second.js")
      ],
      api,
      {
        hostVersion: "1.0.0",
        sdkVersion: "1.0.0",
        loadStyle: async () => undefined,
        importModule: async entry => {
          if (entry.startsWith("/bad.js?")) {
            throw new Error("boom");
          }

          return {
            register(moduleApi) {
              moduleApi.navigation.add({ id: entry, label: entry, path: `/${entry}` });
            }
          };
        }
      }
    );

    expect(api.navigation.list()).toHaveLength(2);
    expect(api.diagnostics.list().map(x => x.status)).toEqual(["loaded", "failed", "loaded"]);
  });

  it("skips incompatible modules before importing them", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    await loadStudioModules(
      [manifest("future", "/future.js", "^2.0.0")],
      api,
      {
        hostVersion: "1.0.0",
        sdkVersion: "1.0.0",
        importModule: async () => {
          throw new Error("should not import");
        }
      }
    );

    expect(api.diagnostics.list()[0].status).toBe("incompatible");
  });

  it("does not register dashboard widgets for skipped modules", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    await loadStudioModules(
      [manifest("future", "/future.js", "^2.0.0")],
      api,
      {
        hostVersion: "1.0.0",
        sdkVersion: "1.0.0",
        importModule: async () => ({
          register(moduleApi) {
            moduleApi.dashboardWidgets.add({ id: "future-widget", title: "Future", component: () => null });
          }
        })
      }
    );

    expect(api.dashboardWidgets.list()).toEqual([]);
  });

  it("adds module versions to imported scripts and styles", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const imported: string[] = [];
    const styles: string[] = [];

    await loadStudioModules(
      [{ ...manifest("versioned", "/module.js?existing=1#main"), styles: ["/module.css"] }],
      api,
      {
        hostVersion: "1.0.0",
        sdkVersion: "1.0.0",
        loadStyle: async href => {
          styles.push(href);
        },
        importModule: async entry => {
          imported.push(entry);
          return { register() {} };
        }
      }
    );

    expect(imported).toEqual(["/module.js?existing=1&studioModuleVersion=1.0.0#main"]);
    expect(styles).toEqual(["/module.css?studioModuleVersion=1.0.0"]);
  });

  it("does not duplicate existing module version query parameters", () => {
    expect(addModuleVersion("/module.js?studioModuleVersion=1.0.0", "1.0.1")).toBe("/module.js?studioModuleVersion=1.0.0");
  });
});

function manifest(id: string, entry: string, host = "^1.0.0") {
  return {
    id,
    displayName: id,
    version: "1.0.0",
    entry,
    styles: [],
    requiredHostVersion: host,
    requiredSdkVersion: "^1.0.0",
    capabilities: []
  };
}
