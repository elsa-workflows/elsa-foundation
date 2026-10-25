import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getWorkflowInstance } from "../api/runtime";
import {
  mapWorkflowInstanceDetails,
  readWorkflowInstanceInspectionGranularity,
  readWorkflowInstancePersistenceMode
} from "../workflowInstanceInspection";
import {
  formatInspectionGranularity,
  WorkflowInstanceInspectionFacts,
  WorkflowInstanceInspectionNote
} from "../workflow-editor/WorkflowInstanceInspectionFacts";
import type { WorkflowInstanceDetails } from "../workflowTypes";

let mounted: { root: Root; container: HTMLDivElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("workflow instance inspection mapping", () => {
  it("keeps recognized cadence and granularity values", () => {
    expect(mapWorkflowInstanceDetails(details({
      persistenceMode: "Durable",
      inspectionGranularity: "activity-level"
    }))).toMatchObject({
      persistenceMode: "Durable",
      inspectionGranularity: "activity-level"
    });
    expect(mapWorkflowInstanceDetails(details({
      persistenceMode: "Balanced",
      inspectionGranularity: "boundary-level"
    }))).toMatchObject({
      persistenceMode: "Balanced",
      inspectionGranularity: "boundary-level"
    });
    expect(mapWorkflowInstanceDetails(details({
      persistenceMode: "Ephemeral",
      inspectionGranularity: "boundary-level"
    }))).toMatchObject({
      persistenceMode: "Ephemeral",
      inspectionGranularity: "boundary-level"
    });
  });

  it("drops missing, null, and unrecognized values so older hosts stay empty", () => {
    expect(mapWorkflowInstanceDetails(details())).toMatchObject({
      persistenceMode: undefined,
      inspectionGranularity: undefined
    });
    expect(mapWorkflowInstanceDetails(details({
      persistenceMode: null,
      inspectionGranularity: null
    }))).toMatchObject({
      persistenceMode: undefined,
      inspectionGranularity: undefined
    });
    expect(readWorkflowInstancePersistenceMode("balanced")).toBeUndefined();
    expect(readWorkflowInstanceInspectionGranularity("Activity-level")).toBeUndefined();
    expect(mapWorkflowInstanceDetails(details({
      persistenceMode: "Coalesced" as WorkflowInstanceDetails["persistenceMode"],
      inspectionGranularity: "folded" as WorkflowInstanceDetails["inspectionGranularity"]
    }))).toMatchObject({
      persistenceMode: undefined,
      inspectionGranularity: undefined
    });
  });

  it("maps the two fields from getWorkflowInstance and ignores them when the host omits them", async () => {
    const withFields = details({ persistenceMode: "Balanced", inspectionGranularity: "boundary-level" });
    const withoutFields = details();
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("capabilities")) return instanceCapabilities;
      return url.includes("wf-legacy") ? withoutFields : withFields;
    });

    await expect(getWorkflowInstance(context({ getJson }), "wf-1")).resolves.toMatchObject({
      persistenceMode: "Balanced",
      inspectionGranularity: "boundary-level"
    });
    await expect(getWorkflowInstance(context({ getJson }), "wf-legacy")).resolves.toMatchObject({
      persistenceMode: undefined,
      inspectionGranularity: undefined
    });
  });
});

describe("workflow instance inspection facts", () => {
  it("shows a Durable run as activity-level", () => {
    const { container } = mountFacts({
      persistenceMode: "Durable",
      inspectionGranularity: "activity-level"
    });

    expect(container.textContent).toContain("Checkpoint cadence");
    expect(container.textContent).toContain("Durable");
    expect(container.textContent).toContain("Inspection");
    expect(container.textContent).toContain("Activity-level");
    expect(container.textContent).not.toContain("folded to the commit boundary");
    expect(valueFor(container, "Checkpoint cadence")).toBe("Durable");
    expect(valueFor(container, "Inspection")).toBe("Activity-level");
    expect(dd(container, "Durable").getAttribute("title")).toContain("checkpoints after every activity");
    expect(dd(container, "Activity-level").getAttribute("title")).toContain("exact");
  });

  it.each(["Balanced", "Ephemeral"] as const)("shows a %s run as boundary-level with the folded-evidence explanation", mode => {
    const { container } = mountFacts({
      persistenceMode: mode,
      inspectionGranularity: "boundary-level"
    });

    expect(container.textContent).toContain("Checkpoint cadence");
    expect(container.textContent).toContain(mode);
    expect(container.textContent).toContain("Boundary-level");
    expect(container.textContent).toContain("folded to the commit boundary");
    expect(dd(container, mode).getAttribute("title")).toContain("boundary-level detail");
    expect(dd(container, "Boundary-level").getAttribute("title")).toContain("folded to the commit boundary");
    expect(container.querySelector("[role='note']")?.textContent).toContain("timeline values reflect the durable boundary");
    expect(formatInspectionGranularity("boundary-level")).toBe("Boundary-level");
  });

  it("renders nothing when the host omits the fields", () => {
    const { container } = mountFacts({});

    expect(container.textContent).toBe("");
    expect(container.querySelector("dt")).toBeNull();
    expect(container.querySelector("[role='note']")).toBeNull();
  });
});

function mountFacts(facts: Pick<WorkflowInstanceDetails, "persistenceMode" | "inspectionGranularity">) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <>
      <dl>
        <WorkflowInstanceInspectionFacts
          persistenceMode={facts.persistenceMode}
          inspectionGranularity={facts.inspectionGranularity}
        />
      </dl>
      <WorkflowInstanceInspectionNote inspectionGranularity={facts.inspectionGranularity} />
    </>
  ));
  mounted = { root, container };
  return { container };
}

function details(overrides: Partial<WorkflowInstanceDetails> = {}): WorkflowInstanceDetails {
  return {
    instance: {
      workflowExecutionId: "wf-1",
      artifactId: "artifact-1",
      artifactVersion: "1.0.0",
      artifactHash: "sha256:1",
      definitionId: "definition-1",
      definitionVersionId: "version-1",
      status: "Completed",
      createdAt: "2026-10-20T09:00:00Z",
      activityCount: 0,
      incidentCount: 0
    },
    activities: [],
    ...overrides
  };
}

function valueFor(container: HTMLElement, label: string) {
  const term = [...container.querySelectorAll("dt")].find(item => item.textContent === label);
  return term?.nextElementSibling?.textContent ?? "";
}

function dd(container: HTMLElement, value: string) {
  return [...container.querySelectorAll("dd")].find(item => item.textContent === value)!;
}

function context(http: Record<string, unknown>) {
  return { baseUrl: `test://instance-inspection-${Math.random()}`, http } as unknown as StudioEndpointContext;
}

const instanceCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [{ rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true }]
  }]
};
