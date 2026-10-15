import { beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../api/workflowDesign";
import { formatNamedIdentityTitle, loadDefinitionDisplayNames, resolveNamedIdentity, uniqueDefinitionIds } from "../workflow-editor/definitionDisplayNames";

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
    expect(listDefinitions).toHaveBeenCalledTimes(1);
    expect(listDefinitions).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ pageSize: 100, state: "all" }));
    expect(getDefinition).toHaveBeenCalledWith(expect.anything(), "def-2");
  });
});
