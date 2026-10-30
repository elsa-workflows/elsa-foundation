import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { AgentToolResults } from "../AgentToolResults";

it("keeps owner evidence visible when a contributed renderer fails", () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  const container = document.createElement("div"); const root = createRoot(container);
  const target = { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r2" };
  const payload = { typeId: "sample.changed", schemaVersion: "1", data: { value: 42 } };
  try {
    flushSync(() => root.render(<AgentToolResults results={[{ toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Observed owner result.", receipt: { invocationId: "i", before: { ...target, revision: "r1" }, after: target, scope: "serverPersisted", evidence: payload } }]} renderers={[{ id: "sample", moduleId: "Sample", displayName: "Sample", resultTypes: ["sample.changed"], component: () => { throw new Error("Broken module renderer"); } }]} />));
    expect(container.textContent).toContain("Observed target: one"); expect(container.textContent).toContain("r2");
    expect(container.textContent).toContain("Structured evidence remains available"); expect(container.textContent).toContain('"value": 42');
  } finally { flushSync(() => root.unmount()); error.mockRestore(); }
});
