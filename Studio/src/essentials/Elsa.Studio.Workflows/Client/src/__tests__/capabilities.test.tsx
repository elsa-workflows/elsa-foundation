import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  capabilityIds,
  clearApiCapabilityCache,
  getApiCapabilities,
  invalidateApiCapabilityCache,
  hasApiCapability,
  resolveCapabilityLink
} from "../api/capabilities";
import { getActivityInputOptions } from "../api/workflowDesign";

afterEach(clearApiCapabilityCache);

function context(getJson: ReturnType<typeof vi.fn>, postJson = vi.fn()) {
  return { baseUrl: "https://server.example/shell-a", http: { getJson, postJson } } as unknown as StudioEndpointContext;
}

describe("API capability bootstrap", () => {
  it("coalesces concurrent consumers into one authenticated shell request and reuses the document", async () => {
    const document = { capabilities: [{
      id: capabilityIds.workflowDesign,
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }] };
    const getJson = vi.fn().mockResolvedValue(document);
    const value = context(getJson);

    const [first, second, link] = await Promise.all([
      getApiCapabilities(value),
      getApiCapabilities(value),
      resolveCapabilityLink(value, capabilityIds.workflowDesign, "workflow-definitions")
    ]);

    expect(first).toEqual(document);
    expect(second).toBe(first);
    expect(link).toBe("/shell-a/design/workflows/definitions");
    expect(getJson).toHaveBeenCalledOnce();
    expect(getJson).toHaveBeenCalledWith("/shell-a/capabilities");
  });

  it.each(["", "test://custom-transport", "options-test"])("retains root capability paths for a custom transport with base %s", async baseUrl => {
    const getJson = vi.fn().mockResolvedValue({ capabilities: [{
      id: capabilityIds.workflowDesign,
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }] });
    const value = { ...context(getJson), baseUrl };

    await expect(resolveCapabilityLink(value, capabilityIds.workflowDesign, "workflow-definitions"))
      .resolves.toBe("/design/workflows/definitions");
    expect(getJson).toHaveBeenCalledWith("/capabilities");
  });

  it("fails explicitly and emits no domain request when a capability is absent", async () => {
    const postJson = vi.fn();
    const value = context(vi.fn().mockResolvedValue({ capabilities: [] }), postJson);

    await expect(getActivityInputOptions(
      value,
      "activity-v1",
      "Choice",
      "node-1",
      {},
      new AbortController().signal
    )).rejects.toEqual(expect.objectContaining({
      name: "ApiCapabilityUnavailableError",
      capabilityId: capabilityIds.workflowDesign
    }));

    expect(postJson).not.toHaveBeenCalled();
  });

  it("rejects an unsupported capability contract major instead of calling it blindly", async () => {
    const value = context(vi.fn().mockResolvedValue({ capabilities: [{
      id: capabilityIds.workflowDesign,
      contractVersion: "2",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }] }));

    await expect(hasApiCapability(value, capabilityIds.workflowDesign)).resolves.toBe(false);
    await expect(resolveCapabilityLink(
      value,
      capabilityIds.workflowDesign,
      "workflow-definitions"
    )).rejects.toMatchObject({
      name: "ApiCapabilityVersionMismatchError",
      capabilityId: capabilityIds.workflowDesign,
      expectedVersion: "1",
      actualVersion: "2"
    });
  });

  it("fails explicitly and emits no domain request when an optional link is absent", async () => {
    const postJson = vi.fn();
    const value = context(vi.fn().mockResolvedValue({ capabilities: [{
      id: capabilityIds.workflowDesign,
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }] }), postJson);

    await expect(getActivityInputOptions(
      value,
      "activity-v1",
      "Choice",
      "node-1",
      {},
      new AbortController().signal
    )).rejects.toMatchObject({
      name: "ApiCapabilityUnavailableError",
      relation: "activity-input-options"
    });

    expect(postJson).not.toHaveBeenCalled();
  });

  it("evicts a failed bootstrap so an explicit retry can recover", async () => {
    const document = { capabilities: [] };
    const getJson = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce(document);
    const value = context(getJson);

    await expect(getApiCapabilities(value)).rejects.toThrow("offline");
    await expect(getApiCapabilities(value)).resolves.toEqual(document);
    expect(getJson).toHaveBeenCalledTimes(2);
  });

  it("invalidates only the explicitly refreshed backend's cached capability document", async () => {
    const oldDocument = { capabilities: [] };
    const refreshedDocument = { capabilities: [{
      id: capabilityIds.publishing,
      contractVersion: "1",
      links: [{ rel: "workflow-draft-test-run-options", href: "publishing/test-run-options" }]
    }] };
    const firstGetJson = vi.fn().mockResolvedValue(oldDocument);
    const secondGetJson = vi.fn().mockResolvedValue(oldDocument);
    const first = context(firstGetJson);
    const second = { ...context(secondGetJson), baseUrl: "https://server.example/shell-b" };
    await getApiCapabilities(first);
    await getApiCapabilities(second);

    invalidateApiCapabilityCache(first);
    firstGetJson.mockResolvedValueOnce(refreshedDocument);
    await expect(getApiCapabilities(first)).resolves.toEqual(refreshedDocument);
    await expect(getApiCapabilities(second)).resolves.toEqual(oldDocument);

    expect(firstGetJson).toHaveBeenCalledTimes(2);
    expect(secondGetJson).toHaveBeenCalledOnce();
  });
});
