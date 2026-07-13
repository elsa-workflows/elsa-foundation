import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { listWorkflowInstances, workflowInstanceListQuery } from "../api/runtime";

afterEach(clearApiCapabilityCache);

describe("workflow instance list client", () => {
  it("constructs the complete operational filter and cursor query", () => {
    const query = workflowInstanceListQuery({
      status: "Faulted",
      runKind: "PublishedRun",
      definitionId: "definition/1",
      workflowExecutionId: "execution 1",
      artifactId: "artifact+1",
      correlationId: "correlation&1",
      from: "2026-07-01T10:00:00.000Z",
      to: "2026-07-02T10:00:00.000Z",
      take: 50,
      cursor: "opaque/cursor+value="
    });

    expect(Object.fromEntries(new URLSearchParams(query))).toEqual({
      status: "Faulted",
      runKind: "PublishedRun",
      definitionId: "definition/1",
      workflowExecutionId: "execution 1",
      artifactId: "artifact+1",
      correlationId: "correlation&1",
      from: "2026-07-01T10:00:00.000Z",
      to: "2026-07-02T10:00:00.000Z",
      take: "50",
      cursor: "opaque/cursor+value="
    });
  });

  it("preserves cursor metadata and normalizes the legacy array response", async () => {
    const pageContext = context(async url => url === "/capabilities" ? capabilities : {
      items: [instance("execution-1")],
      nextCursor: "next",
      hasNext: true,
      count: 1,
      totalCount: 3
    });
    const legacyContext = context(async url => url === "/capabilities" ? capabilities : [instance("legacy")]);

    await expect(listWorkflowInstances(pageContext, { take: 1 })).resolves.toMatchObject({
      nextCursor: "next",
      hasNext: true,
      count: 1,
      totalCount: 3
    });
    await expect(listWorkflowInstances(legacyContext)).resolves.toMatchObject({
      items: [{ workflowExecutionId: "legacy" }],
      hasNext: false,
      totalCount: 1
    });
  });
});

const capabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{ rel: "workflow-instances", href: "runtime/workflows/instances" }]
  }]
};

function context(getJson: (url: string) => Promise<unknown>) {
  return {
    baseUrl: `test://workflow-instances-${Math.random()}`,
    http: { getJson: vi.fn(getJson) }
  } as unknown as StudioEndpointContext;
}

function instance(workflowExecutionId: string) {
  return {
    workflowExecutionId,
    artifactId: "artifact-1",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:test",
    status: "Completed",
    createdAt: "2026-07-01T00:00:00Z",
    activityCount: 0,
    incidentCount: 0
  };
}
