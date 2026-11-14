import { describe, expect, it } from "vitest";
import { desiredFeatureState } from "../../../../../../tests/acceptance/weaverInventorySelection";

describe("normal inventory native Apply", () => {
  it("retains all enabled native configurations and secret placeholders while deselecting both log features", () => {
    const configuration = { EncryptionKey: "********", Nested: { keep: true } };
    const catalog = [
      { id: "Unrelated", enabled: true, configuration },
      { id: "DiagnosticsStructuredLogs", enabled: true, configuration: { keep: 1 } },
      { id: "DiagnosticsStructuredLogsEntityFrameworkCore", enabled: true, configuration: {} },
      { id: "OtherDisabled", enabled: false, configuration: {} }
    ];
    const desired = desiredFeatureState(catalog, { DiagnosticsStructuredLogs: false, DiagnosticsStructuredLogsEntityFrameworkCore: false });
    expect(desired).toEqual([catalog[0], { ...catalog[1], enabled: false }, { ...catalog[2], enabled: false }]);
    expect(desired[0].configuration).toBe(configuration);
    expect(catalog[1].enabled).toBe(true);
  });

  it("adds a discovered disabled Studio feature while retaining unrelated selections", () => {
    const current = { id: "Current", enabled: true, configuration: { unchanged: "native" } };
    const extension = { id: "ExtensionBuilderStudio", enabled: false, configuration: {} };
    expect(desiredFeatureState([current, extension], { ExtensionBuilderStudio: true })).toEqual([current, { ...extension, enabled: true }]);
    expect(() => desiredFeatureState([current], { Unknown: true })).toThrow("discovered native feature");
  });
});
