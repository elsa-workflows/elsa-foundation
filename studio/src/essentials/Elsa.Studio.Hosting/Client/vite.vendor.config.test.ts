// @vitest-environment node
import { describe, expect, it } from "vitest";
import { externalizeReactExceptShim } from "./vite.vendor.config";

describe("vendor build: react external", () => {
  const windowsShim = "C:\\repo\\Client\\src\\vendor\\react.ts";
  const posixShim = "/repo/Client/src/vendor/react.ts";

  it("bundles React into the shim when the importer uses forward slashes on Windows", () => {
    const external = externalizeReactExceptShim(windowsShim);
    expect(external("react", "C:/repo/Client/src/vendor/react.ts")).toBe(false);
  });

  it("bundles React into the shim when both sides use the same separators", () => {
    expect(externalizeReactExceptShim(windowsShim)("react", windowsShim)).toBe(false);
    expect(externalizeReactExceptShim(posixShim)("react", posixShim)).toBe(false);
  });

  it("keeps React external for every other importer", () => {
    expect(externalizeReactExceptShim(windowsShim)("react", "C:/repo/Client/src/vendor/react-dom.ts")).toBe(true);
    expect(externalizeReactExceptShim(posixShim)("react", "/repo/Client/src/vendor/studio-ui.ts")).toBe(true);
    expect(externalizeReactExceptShim(posixShim)("react", undefined)).toBe(true);
  });

  it("never externalizes other specifiers", () => {
    expect(externalizeReactExceptShim(posixShim)("react/jsx-runtime", "/repo/Client/src/vendor/studio-ui.ts")).toBe(false);
    expect(externalizeReactExceptShim(posixShim)("react-dom", posixShim)).toBe(false);
  });
});
