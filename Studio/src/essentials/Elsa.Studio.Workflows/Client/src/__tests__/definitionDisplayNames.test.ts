import { beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../api/workflowDesign";
import { loadDefinitionDisplayNames, resolveNamedIdentity, uniqueDefinitionIds } from "../workflow-editor/definitionDisplayNames";

vi.mock("../api/workflowDesign", () => ({
  listDefinitions: vi.fn(),
  getDefinition: vi.fn()
}));

describe("definition display names", () => {
  beforeEach(() => {
    vi.mocked(listDefinitions).mockReset();
    vi.mocked(getDefinition).mockReset();
  });

  it("uses the name as the primary label and keeps the id as secondary text", () => {
    expect(resolveNamedIdentity("Order fulfillment", "def-1")).toEqual({
      label: "Order fulfillment",
      secondaryId: "def-1"
    });
    expect(resolveNamedIdentity("  ", "def-1")).toEqual({
      label: "def-1",
      secondaryId: null
    });
    expect(uniqueDefinitionIds(["def-1", "def-1", " ", null, "def-2"])).toEqual(["def-1", "def-2"]);
  });

  it("loads names from the definition list and fills gaps with per-id reads", async () => {
    vi.mocked(listDefinitions).mockResolvedValue({
      definitions: [{ id: "def-1", name: "Order fulfillment", createdAt: "", lastModifiedAt: "", versionCount: 1 }],
      page: 1,
      pageSize: 100,
      totalCount: 1
    });
    vi.mocked(getDefinition).mockResolvedValue({
      definition: { id: "def-2", name: "Invoice approval", createdAt: "", lastModifiedAt: "", versionCount: 1 },
      versions: []
    });
    const names = await loadDefinitionDisplayNames({} as StudioEndpointContext, ["def-1", "def-2"]);
    expect(names.get("def-1")).toBe("Order fulfillment");
    expect(names.get("def-2")).toBe("Invoice approval");
    expect(getDefinition).toHaveBeenCalledWith(expect.anything(), "def-2");
  });
});
