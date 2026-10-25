import { beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../api/workflowDesign";
import type { WorkflowDefinitionDetails, WorkflowDefinitionSummary } from "../workflowTypes";
import {
  definitionNameLookupConcurrency,
  formatNamedIdentityTitle,
  loadDefinitionDisplayNames,
  resolveNamedIdentity,
  uniqueDefinitionIds
} from "../workflow-editor/definitionDisplayNames";

vi.mock("../api/workflowDesign", () => ({
  listDefinitions: vi.fn(),
  getDefinition: vi.fn()
}));

describe("definition display names", () => {
  beforeEach(() => {
    vi.mocked(listDefinitions).mockReset();
    vi.mocked(getDefinition).mockReset();
  });

  it("uses the name as the primary label and keeps both distinct ids as secondary text", () => {
    expect(resolveNamedIdentity(undefined, "def-1", ["version-1"])).toEqual({
      label: "def-1",
      secondaryIds: ["version-1"]
    });
    expect(formatNamedIdentityTitle("def-1", ["version-1"])).toBe("def-1 (version-1)");
    expect(resolveNamedIdentity("Order fulfillment", "def-1", ["version-1"])).toEqual({
      label: "Order fulfillment",
      secondaryIds: ["def-1", "version-1"]
    });
    expect(formatNamedIdentityTitle("Order fulfillment", ["def-1", "version-1"]))
      .toBe("Order fulfillment (def-1 · version-1)");
    expect(resolveNamedIdentity("  ", "def-1", ["def-1", "version-1"])).toEqual({
      label: "def-1",
      secondaryIds: ["version-1"]
    });
    expect(uniqueDefinitionIds(["def-1", "def-1", " ", null, "def-2"])).toEqual(["def-1", "def-2"]);
  });

  it("loads names from the definition list and fills gaps with per-id reads", async () => {
    vi.mocked(listDefinitions).mockResolvedValue({
      definitions: [summary("def-1", "Order fulfillment")],
      page: 1,
      pageSize: 100,
      totalCount: 1
    });
    vi.mocked(getDefinition).mockResolvedValue(details("def-2", "Invoice approval"));
    const names = await loadDefinitionDisplayNames({} as StudioEndpointContext, ["def-1", "def-2"]);
    expect(names.get("def-1")).toBe("Order fulfillment");
    expect(names.get("def-2")).toBe("Invoice approval");
    expect(listDefinitions).toHaveBeenCalledTimes(1);
    expect(listDefinitions).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ pageSize: 100, state: "all" }));
    expect(getDefinition).toHaveBeenCalledWith(expect.anything(), "def-2");
  });

  it("caps in-flight per-id lookups when more than 100 definitions miss the list page", async () => {
    const ids = Array.from({ length: 120 }, (_, index) => `def-${index + 1}`);
    vi.mocked(listDefinitions).mockResolvedValue({
      definitions: ids.slice(0, 100).map(id => summary(id, `Listed ${id}`)),
      page: 1,
      pageSize: 120,
      totalCount: 120
    });

    let inFlight = 0;
    let maxInFlight = 0;
    vi.mocked(getDefinition).mockImplementation(async (_context, id) => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await Promise.resolve();
      inFlight -= 1;
      return details(id, `Looked up ${id}`);
    });

    const names = await loadDefinitionDisplayNames({} as StudioEndpointContext, ids);

    expect(getDefinition).toHaveBeenCalledTimes(20);
    expect(definitionNameLookupConcurrency).toBe(6);
    expect(maxInFlight).toBeGreaterThan(1);
    expect(maxInFlight).toBeLessThanOrEqual(6);
    expect(maxInFlight).toBeLessThanOrEqual(definitionNameLookupConcurrency);
    expect(names.size).toBe(120);
    expect(names.get("def-1")).toBe("Listed def-1");
    expect(names.get("def-100")).toBe("Listed def-100");
    expect(names.get("def-101")).toBe("Looked up def-101");
    expect(names.get("def-120")).toBe("Looked up def-120");
  });

  it("falls back to capped per-id reads and skips failed or blank names when the list call fails", async () => {
    const ids = Array.from({ length: 110 }, (_, index) => `def-${index + 1}`);
    vi.mocked(listDefinitions).mockRejectedValue(new Error("Design is unavailable"));

    let inFlight = 0;
    let maxInFlight = 0;
    vi.mocked(getDefinition).mockImplementation(async (_context, id) => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await Promise.resolve();
      inFlight -= 1;
      if (id === "def-3") throw new Error("not found");
      if (id === "def-4") return details(id, "  ");
      return details(id, `Looked up ${id}`);
    });

    const names = await loadDefinitionDisplayNames({} as StudioEndpointContext, ids);

    expect(getDefinition).toHaveBeenCalledTimes(110);
    expect(definitionNameLookupConcurrency).toBe(6);
    expect(maxInFlight).toBeGreaterThan(1);
    expect(maxInFlight).toBeLessThanOrEqual(6);
    expect(maxInFlight).toBeLessThanOrEqual(definitionNameLookupConcurrency);
    expect(names.get("def-1")).toBe("Looked up def-1");
    expect(names.get("def-3")).toBeUndefined();
    expect(names.get("def-4")).toBeUndefined();
    expect(names.size).toBe(108);
  });

  it("does not fall through to per-id reads when the caller aborts the list call", async () => {
    const controller = new AbortController();
    vi.mocked(listDefinitions).mockImplementation(async (_context, _request, signal) => {
      controller.abort();
      if (signal?.aborted) throw new DOMException("Aborted", "AbortError");
      return { definitions: [], page: 1, pageSize: 100, totalCount: 0 };
    });

    const names = await loadDefinitionDisplayNames(
      {} as StudioEndpointContext,
      ["def-1", "def-2"],
      controller.signal
    );

    expect(names.size).toBe(0);
    expect(getDefinition).not.toHaveBeenCalled();
  });
});

function summary(id: string, name: string): WorkflowDefinitionSummary {
  return { id, name, createdAt: "", lastModifiedAt: "", versionCount: 1 };
}

function details(id: string, name: string): WorkflowDefinitionDetails {
  return { definition: summary(id, name), versions: [] };
}
