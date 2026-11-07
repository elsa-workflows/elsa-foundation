import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { requireBpmnDocumentAuthoring } from "../api/bpmnInterchange";

describe("complete-source backend compatibility", () => {
  it("requires the exact capability response before enabling document editing", async () => {
    const getJson = vi.fn().mockResolvedValue({ structureKind: "elsa.bpmn.structure", schemaVersion: "2.0.0" });
    const context = { http: { getJson } } as unknown as StudioEndpointContext;
    const controller = new AbortController();
    await requireBpmnDocumentAuthoring(context, controller.signal);
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith("interchange/bpmn/authoring-capabilities", { signal: controller.signal });
  });

  it.each([null, {}, { structureKind: "foreign", schemaVersion: "2.0.0" }, { structureKind: "elsa.bpmn.structure", schemaVersion: "1.0.0" },
    { structureKind: "elsa.bpmn.structure", schemaVersion: "99" }])("refuses an absent or incompatible contract %j", async response => {
    const context = { http: { getJson: vi.fn().mockResolvedValue(response) } } as unknown as StudioEndpointContext;
    await expect(requireBpmnDocumentAuthoring(context)).rejects.toThrow("incompatible BPMN authoring contract");
  });

  it.each([404, 403, 500])("retains the draft when capability discovery fails with %s", async status => {
    const context = { http: { getJson: vi.fn().mockRejectedValue(new Error(`Private HTTP detail ${status}`)) } } as unknown as StudioEndpointContext;
    await expect(requireBpmnDocumentAuthoring(context)).rejects.toThrow("compatible backend");
    await expect(requireBpmnDocumentAuthoring(context)).rejects.not.toThrow("Private HTTP detail");
  });

  it("preserves cancellation so an obsolete authority probe cannot enable edits", async () => {
    const controller = new AbortController(), failure = new DOMException("Aborted", "AbortError");
    controller.abort(failure);
    const context = { http: { getJson: vi.fn().mockRejectedValue(failure) } } as unknown as StudioEndpointContext;
    await expect(requireBpmnDocumentAuthoring(context, controller.signal)).rejects.toBe(failure);
  });

  it("checks cancellation again when an HTTP transport resolves an obsolete probe", async () => {
    let resolve!: (value: unknown) => void;
    const controller = new AbortController(), failure = new DOMException("Obsolete", "AbortError");
    const context = { http: { getJson: vi.fn(() => new Promise(value => { resolve = value; })) } } as unknown as StudioEndpointContext;
    const pending = requireBpmnDocumentAuthoring(context, controller.signal);
    controller.abort(failure);
    resolve({ structureKind: "elsa.bpmn.structure", schemaVersion: "2.0.0" });
    await expect(pending).rejects.toBe(failure);
  });
});
