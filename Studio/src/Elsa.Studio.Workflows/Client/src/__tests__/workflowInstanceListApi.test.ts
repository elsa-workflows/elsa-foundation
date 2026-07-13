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

  it("prefers the advertised paged relation and preserves its cursor metadata", async () => {
    const pageContext = context(async url => url === "/capabilities" ? pagedCapabilities : {
      items: [instance("execution-1")],
      nextCursor: "next",
      hasNext: true,
      count: 1,
      totalCount: 3
    });

    await expect(listWorkflowInstances(pageContext, { take: 1 })).resolves.toMatchObject({
      nextCursor: "next",
      hasNext: true,
      count: 1,
      totalCount: 3
    });
    expect(pageContext.http.getJson).toHaveBeenLastCalledWith("/runtime/workflows/instances/page?take=1");
  });

  it("falls back to the v1 array relation and normalizes its result", async () => {
    const legacyContext = context(async url => url === "/capabilities" ? legacyCapabilities : [instance("legacy")]);

    await expect(listWorkflowInstances(legacyContext)).resolves.toMatchObject({
      items: [{ workflowExecutionId: "legacy" }],
      hasNext: false,
      totalCount: 1
    });
    expect(legacyContext.http.getJson).toHaveBeenLastCalledWith("/runtime/workflows/instances");
  });

  it("does not silently infer a paged contract from the legacy v1 relation", async () => {
    const legacyContext = context(async url => url === "/capabilities" ? legacyCapabilities : {
      items: [instance("unexpected-page")],
      nextCursor: "next"
    });

    await expect(listWorkflowInstances(legacyContext)).rejects.toThrow(
      "Legacy workflow instance relation must return an array."
    );
  });
});

const legacyCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{ rel: "workflow-instances", href: "runtime/workflows/instances" }]
  }]
};

const pagedCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instances", href: "runtime/workflows/instances" },
      { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" }
    ]
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
