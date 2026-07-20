import { describe, expect, it } from "vitest";
import {
  builtInConversionProfiles,
  decorateConversionDiagnostic,
  describeInferredSource,
  formatTypeContract,
  readConversionMode,
  readConversionPlan,
  readConversionProfile,
  shortenFingerprint,
  withConversionMode,
  withConversionProfile
} from "../conversionSettings";

describe("authored conversion requests", () => {
  it("reads the authored mode case-insensitively and defaults absent/unknown modes to auto", () => {
    expect(readConversionMode(undefined)).toBe("auto");
    expect(readConversionMode(null)).toBe("auto");
    expect(readConversionMode({})).toBe("auto");
    expect(readConversionMode({ mode: "json" })).toBe("json");
    expect(readConversionMode({ mode: "Json" })).toBe("json");
    expect(readConversionMode({ mode: "XML" })).toBe("xml");
    expect(readConversionMode({ mode: "someday-mode" })).toBe("auto");
  });

  it("collapses a pure Auto selection to null so the argument drops the request (legacy default)", () => {
    expect(withConversionMode(undefined, "auto")).toBeNull();
    expect(withConversionMode({ mode: "json" }, "auto")).toBeNull();
  });

  it("keeps unknown forward-compatible fields when switching modes", () => {
    const request = { mode: "json", limits: { maxDepth: 4 }, futureField: true };

    const next = withConversionMode(request, "xml");
    expect(next).toEqual({ mode: "xml", limits: { maxDepth: 4 }, futureField: true });

    // Returning to Auto keeps the request because limits/unknowns are still authored.
    expect(withConversionMode(next, "auto")).toEqual({ mode: "auto", limits: { maxDepth: 4 }, futureField: true });
  });

  it("drops a stale profile reference when leaving Profile mode and writes one when entering it", () => {
    const withProfile = withConversionProfile({ mode: "json" }, { id: "partner.customer-json", version: "3" });
    expect(withProfile).toEqual({ mode: "profile", profile: { id: "partner.customer-json", version: "3" } });

    expect(withConversionMode(withProfile, "none")).toEqual({ mode: "none" });
  });

  it("reads profile references tolerantly", () => {
    expect(readConversionProfile({ profile: { id: "elsa.json", version: "1" } })).toEqual({ id: "elsa.json", version: "1" });
    expect(readConversionProfile({ profile: {} })).toBeNull();
    expect(readConversionProfile({})).toBeNull();
  });

  it("ships the foundation built-in profiles as the offline fallback", () => {
    expect(builtInConversionProfiles).toEqual([
      { id: "elsa.json", version: "1" },
      { id: "elsa.xml", version: "1" }
    ]);
  });

  it("infers a source caption from the authored expression and selected mode", () => {
    expect(describeInferredSource("Literal", "plain text", "auto")).toBe("Text");
    expect(describeInferredSource("Literal", { name: "Grace" }, "auto")).toBe("Structured value");
    expect(describeInferredSource("Literal", '{"name":"Grace"}', "json")).toBe("JSON content");
    expect(describeInferredSource("Variable", { referenceKey: "v1" }, "auto")).toBe("Variable value");
    expect(describeInferredSource("JavaScript", "user.name", "auto")).toBe("Expression result");
  });
});

describe("compiled conversion-plan inspection", () => {
  // Matches the foundation compiler-golden wire shape: camelCase properties, PascalCase enum values.
  const plan = {
    schemaVersion: 1,
    sourceRepresentation: "TextValue",
    sourceType: { alias: "String", collectionKind: "Single", schema: null, schemaVersion: null },
    targetType: { alias: "String", collectionKind: "List", schema: null, schemaVersion: null },
    mode: "Json",
    operation: "Profile",
    profile: { id: "elsa.json", version: "1" },
    limits: { maxDepth: 64, maxCollectionItems: 10000, maxPayloadBytes: 1048576 },
    options: {},
    fingerprint: "sha256:8a80851546a1f1875cf5c1d78dbbc51ac6e42d8f8f47b3f77b6e5db34f01c3cb"
  };

  it("summarizes the pinned plan for display", () => {
    expect(readConversionPlan(plan)).toEqual({
      mode: "Json",
      operation: "Profile",
      profile: { id: "elsa.json", version: "1" },
      fingerprint: plan.fingerprint,
      sourceRepresentation: "Text",
      sourceContract: "String",
      targetContract: "String (List)"
    });
  });

  it("returns null for absent or shapeless plans", () => {
    expect(readConversionPlan(undefined)).toBeNull();
    expect(readConversionPlan(null)).toBeNull();
    expect(readConversionPlan({})).toBeNull();
    expect(readConversionPlan("Auto")).toBeNull();
  });

  it("formats type contracts and shortens fingerprints", () => {
    expect(formatTypeContract({ alias: "Customer", collectionKind: "Single" })).toBe("Customer");
    expect(formatTypeContract({ alias: "Int32", collectionKind: "Array" })).toBe("Int32 (Array)");
    expect(formatTypeContract(null)).toBeNull();
    expect(shortenFingerprint(plan.fingerprint)).toBe("sha256:8a80851546a1…");
    expect(shortenFingerprint("short")).toBe("short");
  });
});

describe("conversion diagnostics", () => {
  it("appends actionable guidance to VF-COER-001 publish rejections and leaves other messages alone", () => {
    const message = "VF-COER-001: Cannot resolve conversion from source representation 'TextValue' … using mode 'None'.";
    expect(decorateConversionDiagnostic(message)).toContain("Conversion setting");
    expect(decorateConversionDiagnostic("Something else failed.")).toBe("Something else failed.");
  });
});
