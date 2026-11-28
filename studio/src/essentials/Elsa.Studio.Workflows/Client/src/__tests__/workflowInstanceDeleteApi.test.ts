import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { deleteWorkflowInstance, supportsWorkflowInstanceDeletion } from "../api/runtime";

afterEach(clearApiCapabilityCache);

describe("workflow instance deletion client", () => {
  it("uses the advertised templated relation and validates the response", async () => {
    const deleteJson = vi.fn(async () => ({ workflowExecutionId: "run/1", status: "Deleted" }));
    const client = context(deleteJson, deletionCapabilities);

    await expect(deleteWorkflowInstance(client, "run/1")).resolves.toEqual({
      workflowExecutionId: "run/1",
      status: "Deleted"
    });

    expect(deleteJson).toHaveBeenCalledWith("/runtime/workflows/instances/run%2F1");
  });

  it("does not claim deletion support or call an endpoint when the relation is absent", async () => {
    const deleteJson = vi.fn(async () => ({ workflowExecutionId: "run-1", status: "Deleted" }));
    const client = context(deleteJson, legacyCapabilities);

    await expect(supportsWorkflowInstanceDeletion(client)).resolves.toBe(false);
    await expect(deleteWorkflowInstance(client, "run-1")).rejects.toThrow("workflow-instance-delete");
    expect(deleteJson).not.toHaveBeenCalled();
  });

  it("does not claim success for an absent or resource-denied run", async () => {
    const deleteJson = vi.fn(async () => { throw { status: 404 }; });

    await expect(deleteWorkflowInstance(context(deleteJson, deletionCapabilities), "run-1"))
      .rejects.toThrow("The run is unavailable or you do not have permission to delete it.");
  });

  it("preserves other failures", async () => {
    const deleteJson = vi.fn(async () => { throw new Error("Runtime unavailable"); });

    await expect(deleteWorkflowInstance(context(deleteJson, deletionCapabilities), "run-1"))
      .rejects.toThrow("Runtime unavailable");
  });
});

function context(
  deleteJson: (url: string) => Promise<unknown>,
  capabilities: unknown
) {
  const getJson = vi.fn(async () => capabilities);
  return {
    baseUrl: `test://workflow-instance-delete-${Math.random()}`,
    http: { getJson, deleteJson: vi.fn(deleteJson) }
  } as unknown as StudioEndpointContext;
}

const legacyCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{ rel: "workflow-instances", href: "runtime/workflows/instances" }]
  }]
};

const deletionCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{
      rel: "workflow-instance-delete",
      href: "runtime/workflows/instances/{workflowExecutionId}",
      templated: true
    }]
  }]
};
