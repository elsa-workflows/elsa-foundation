import { describe, expect, it } from "vitest";
import {
  describeExecutableReferenceSelection,
  executableInspectorFallbackTitle,
  formatExecutableInspectorPageTitle,
  resolveExecutableInspectorDisplayName
} from "../workflow-editor/executableInspectorIdentity";

describe("executable inspector identity", () => {
  it("falls back to Executable plus the artifact ID while the definition name is unknown", () => {
    expect(executableInspectorFallbackTitle("artifact-95bc4f13586a")).toBe("Executable artifact-95bc4f13586a");
    expect(executableInspectorFallbackTitle("  ")).toBe("Executable");
    expect(executableInspectorFallbackTitle(undefined)).toBe("Executable");
    expect(resolveExecutableInspectorDisplayName(null, "artifact-95bc4f13586a")).toBe("Executable artifact-95bc4f13586a");
    expect(resolveExecutableInspectorDisplayName("  ", "artifact-95bc4f13586a")).toBe("Executable artifact-95bc4f13586a");
    expect(formatExecutableInspectorPageTitle(null, "artifact-95bc4f13586a", "1.0.0")).toBe("Executable artifact-95bc4f13586a");
  });

  it("uses the workflow display name and version once the source definition is ready", () => {
    expect(resolveExecutableInspectorDisplayName("Invoice settlement", "artifact-95bc4f13586a")).toBe("Invoice settlement");
    expect(formatExecutableInspectorPageTitle("Invoice settlement", "artifact-95bc4f13586a", "1.0.0")).toBe("Invoice settlement · 1.0.0");
    expect(formatExecutableInspectorPageTitle("Invoice settlement", "artifact-95bc4f13586a", "  ")).toBe("Invoice settlement");
  });

  it("describes the chosen reference for the truncated selection caption", () => {
    expect(describeExecutableReferenceSelection("requested", "activation-ref:publication-1"))
      .toBe("Showing requested reference activation-ref:publication-1");
    expect(describeExecutableReferenceSelection("newest-live", "ref-2")).toBe("Showing newest live reference ref-2");
    expect(describeExecutableReferenceSelection("newest", "ref-3")).toBe("Showing newest reference ref-3 (no live references remain)");
    expect(describeExecutableReferenceSelection("other", "ref-4")).toBe("Showing reference ref-4");
  });
});
