import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { canReviewDefinition, IncidentNextStep, useDefinitionReviewAccess } from "../workflow-editor/IncidentNextStep";
import type { IncidentNextStepGuidance } from "../workflow-editor/useOperatorActions";
import type { IncidentStateSummary, WorkflowInstanceSummary } from "../workflowTypes";

let active: { root: Root; container: HTMLDivElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

describe("incident next-step definition handoff", () => {
  it("shares one exact read observation between the run action and incident handoff", async () => {
    const { context, getJson, mutations } = definitionContext("shared-readable", id => Promise.resolve(definition(id)));
    const onOpenDefinition = vi.fn();
    const container = renderSharedDefinitionSurface(context, summary(), incident(), onOpenDefinition);

    await vi.waitFor(() => expect([...container.querySelectorAll("button")]
      .filter(candidate => candidate.textContent === "Review workflow definition")).toHaveLength(2));
    expect(getJson.mock.calls.map(call => call[0])).toEqual([
      "/shared-readable/capabilities",
      "/shared-readable/design/workflows/definitions/definition-1"
    ]);
    const incidentHandoff = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .filter(candidate => candidate.textContent === "Review workflow definition")[1]!;
    click(incidentHandoff);
    expect(onOpenDefinition).toHaveBeenCalledWith("definition-1");
    expect(mutations.postJson).not.toHaveBeenCalled();
    expect(mutations.putJson).not.toHaveBeenCalled();
    expect(mutations.deleteJson).not.toHaveBeenCalled();
  });

  it("keeps both the run action and incident handoff hidden when the exact read is denied", async () => {
    const { context, getJson } = definitionContext("shared-denied", () => Promise.reject(new Error("403")));
    const container = renderSharedDefinitionSurface(context, summary(), incident());

    await vi.waitFor(() => expect(getJson.mock.calls.some(call => String(call[0]).includes("/design/workflows/definitions/"))).toBe(true));
    await Promise.resolve();
    await Promise.resolve();
    expect([...container.querySelectorAll("button")]
      .filter(candidate => candidate.textContent === "Review workflow definition")).toHaveLength(0);
    expect(container.textContent).not.toMatch(/403|permission|forbidden|manage/i);
  });

  it("offers the exact run definition after a read-only matching GET", async () => {
    const { context, getJson, mutations } = definitionContext("readable", id => Promise.resolve(definition(id)));
    const onOpenDefinition = vi.fn();
    const container = render(context, summary(), incident(), onOpenDefinition);

    await vi.waitFor(() => expect(getJson.mock.calls.some(call => String(call[0]).endsWith("/definition-1"))).toBe(true));
    expect(getJson.mock.calls.map(call => call[0])).toEqual([
      "/readable/capabilities",
      "/readable/design/workflows/definitions/definition-1"
    ]);
    await vi.waitFor(() => expect(button(container, "Review workflow definition")).not.toBeNull());

    click(button(container, "Review workflow definition")!);
    expect(onOpenDefinition).toHaveBeenCalledOnce();
    expect(onOpenDefinition).toHaveBeenCalledWith("definition-1");
    expect(container.textContent).toContain("This run keeps using the workflow version it started with. Definition changes affect future runs.");
    expect(mutations.postJson).not.toHaveBeenCalled();
    expect(mutations.putJson).not.toHaveBeenCalled();
    expect(mutations.deleteJson).not.toHaveBeenCalled();
  });

  it.each([
    ["a denied read", (_id: string) => Promise.reject(new Error("403"))],
    ["a mismatched definition response", (_id: string) => Promise.resolve(definition("other-definition"))]
  ])("keeps a review handoff unavailable for %s", async (_label, load) => {
    const { context } = definitionContext(`unconfirmed-${Math.random()}`, load);
    const { getJson } = context.http as unknown as { getJson: ReturnType<typeof vi.fn> };
    const container = render(context, summary(), incident());

    await vi.waitFor(() => expect(getJson.mock.calls.some(call => String(call[0]).includes("/design/workflows/definitions/"))).toBe(true));
    await Promise.resolve();
    await Promise.resolve();
    expect(button(container, "Review workflow definition")).toBeNull();
    expect(container.textContent).not.toMatch(/403|permission|forbidden|manage/i);
  });

  it("ignores a late definition response after the selected run target changes", async () => {
    let resolveFirst!: (value: unknown) => void;
    let resolveSecond!: (value: unknown) => void;
    const { context, getJson } = definitionContext("target-race", id => new Promise(resolve => {
      if (id === "definition-1") resolveFirst = resolve;
      else resolveSecond = resolve;
    }));
    const onOpenDefinition = vi.fn();
    const first = summary();
    const container = render(context, first, incident(), onOpenDefinition);
    await vi.waitFor(() => expect(getJson.mock.calls.some(call => String(call[0]).endsWith("/definition-1"))).toBe(true));

    const second = summary({ workflowExecutionId: "workflow-execution-2", definitionId: "definition-2", artifactId: "artifact-2" });
    const selectedIncident = incident({ workflowExecutionId: "workflow-execution-2", incidentId: "incident-2" });
    rerender(context, second, selectedIncident, onOpenDefinition);
    await vi.waitFor(() => expect(getJson.mock.calls.some(call => String(call[0]).endsWith("/definition-2"))).toBe(true));

    resolveFirst(definition("definition-1"));
    await Promise.resolve();
    expect(button(container, "Review workflow definition")).toBeNull();
    resolveSecond(definition("definition-2"));
    await vi.waitFor(() => expect(button(container, "Review workflow definition")).not.toBeNull());
    click(button(container, "Review workflow definition")!);
    expect(onOpenDefinition).toHaveBeenCalledWith("definition-2");
    expect(container.textContent).toContain("This run keeps using the workflow version it started with.");
    expect(container.textContent).not.toContain("artifact-pinned");
  });

  it("does not request a definition belonging to a different current run", () => {
    const { context, getJson } = definitionContext("run-mismatch", id => Promise.resolve(definition(id)));
    const currentRun = summary();
    render(context, currentRun, incident({ workflowExecutionId: "another-run" }));

    expect(active?.container.textContent).toContain("This incident does not match the run currently open.");
    expect(active?.container.textContent).not.toContain("Definition ID");
    expect(getJson).not.toHaveBeenCalled();
  });

  it("says current run context is unavailable when no run summary was loaded", () => {
    const container = render(undefined, null, incident());

    expect(container.textContent).toContain("The current run context is unavailable here.");
    expect(container.textContent).not.toContain("This incident does not match the run currently open.");
  });
});

describe("incident next-step state guidance", () => {
  it.each([
    ["historical", incident({ status: "Resolved", isBlocking: false }), guidance, "This incident is historical."],
    ["active non-blocking", incident({ isBlocking: false }), guidance, "active but does not block the run"],
    ["another incident is pending", incident(), { ...guidance, pendingIncidentId: "another-incident" }, "An action for another incident in this run is being followed."],
    ["the requester was canceled", incident(), { ...guidance, requesterCancelled: true }, "The requesting activity was canceled."],
    ["a retry is scheduled", incident(), { ...guidance, retryNotBefore: "2026-10-23T09:00:00Z" }, "An automatic retry is scheduled"],
    ["a retry is due", incident(), { ...guidance, retryNotBefore: "2026-10-20T10:00:00Z", retryDue: true }, "An automatic retry is due"]
  ])("keeps %s distinct for the selected incident", (_state, currentIncident, currentGuidance, expectedCopy) => {
    const container = render(undefined, summary(), currentIncident, undefined, currentGuidance);

    expect(container.textContent).toContain("Current run");
    expect(container.textContent).toContain(expectedCopy);
    if (currentGuidance.retryNotBefore) expect(container.textContent).not.toMatch(/\bRetry\b/);
  });

  it("keeps a historically resolved blocking wait from acquiring current-action guidance", () => {
    const historicalWait = incident({
      status: "Resolved",
      isBlocking: true,
      resolutionOutcome: { actionKind: "WaitForIntervention", systemSource: "PoisonedSchedulerWork", metadata: {} }
    });
    const container = render(undefined, summary(), historicalWait, undefined, {
      actionAvailability: "available",
      canAct: true,
      pendingIncidentId: null,
      requesterCancelled: false,
      retryReviewEligibility: { status: "eligible" },
      retryJournalAvailability: "not-required"
    });

    expect(container.textContent).toContain("This incident is historical.");
    expect(container.textContent).not.toContain("Review the run evidence and any available actions below");
  });

  it.each(["Completed", "Faulted", "Cancelled"])("does not describe an available action for an ended %s run", runStatus => {
    const container = render(undefined, summary({ status: runStatus }), incident(), undefined, {
      actionAvailability: "available",
      canAct: true,
      pendingIncidentId: null,
      requesterCancelled: false,
      retryReviewEligibility: { status: "eligible" },
      retryJournalAvailability: "not-required"
    });

    expect(container.textContent).toContain(`This run has ended (${runStatus}).`);
    expect(container.textContent).not.toContain("Review the run evidence and any available actions below");
  });

  it("does not turn unknown or partial retry evidence into a claim that no retry is scheduled", () => {
    const container = render(undefined, summary(), incident(), undefined, { ...guidance, actionAvailability: "available", canAct: true });

    expect(container.textContent).toContain("The exact activity state needed to confirm Retry is unavailable here.");
    expect(container.textContent).not.toMatch(/no automatic retry|no retry is scheduled/i);
  });

  it("explains when an explicitly Scheduled source is not eligible for Retry", () => {
    const container = render(undefined, summary(), incident(), undefined, {
      ...guidance,
      actionAvailability: "available",
      canAct: true,
      retryReviewEligibility: { status: "ineligible", reason: "source-not-faulted", observedStatus: "Scheduled" }
    });

    expect(container.textContent).toContain("The source activity is Scheduled; Retry applies only after it is Faulted.");
  });
});

function render(
  context: StudioEndpointContext | undefined,
  currentSummary: WorkflowInstanceSummary | null,
  currentIncident: IncidentStateSummary,
  onOpenDefinition?: (id: string) => void,
  currentGuidance: IncidentNextStepGuidance = guidance
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(<IncidentNextStep context={context} summary={currentSummary} incident={currentIncident} guidance={currentGuidance} onOpenDefinition={onOpenDefinition} />));
  return container;
}

function renderSharedDefinitionSurface(
  context: StudioEndpointContext,
  currentSummary: WorkflowInstanceSummary,
  currentIncident: IncidentStateSummary,
  onOpenDefinition?: (id: string) => void
) {
  function SharedDefinitionSurface() {
    const access = useDefinitionReviewAccess(context, currentSummary.definitionId);
    return <>
      {canReviewDefinition(access, context, currentSummary.definitionId) ? (
        <button type="button" onClick={() => onOpenDefinition?.(currentSummary.definitionId)}>Review workflow definition</button>
      ) : null}
      <IncidentNextStep
        context={context}
        summary={currentSummary}
        incident={currentIncident}
        guidance={guidance}
        onOpenDefinition={onOpenDefinition}
        definitionAccess={access}
      />
    </>;
  }

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(<SharedDefinitionSurface />));
  return container;
}

function rerender(
  context: StudioEndpointContext,
  currentSummary: WorkflowInstanceSummary,
  currentIncident: IncidentStateSummary,
  onOpenDefinition?: (id: string) => void,
  currentGuidance: IncidentNextStepGuidance = guidance
) {
  flushSync(() => active!.root.render(<IncidentNextStep context={context} summary={currentSummary} incident={currentIncident} guidance={currentGuidance} onOpenDefinition={onOpenDefinition} />));
}

const guidance: IncidentNextStepGuidance = {
  actionAvailability: "unknown",
  canAct: false,
  pendingIncidentId: null,
  requesterCancelled: false,
  retryReviewEligibility: { status: "unknown", reason: "source-not-loaded" },
  retryJournalAvailability: "not-required"
};

function definitionContext(name: string, load: (id: string) => Promise<unknown>) {
  const mutations = { postJson: vi.fn(), putJson: vi.fn(), deleteJson: vi.fn() };
  const getJson = vi.fn(async (url: string) => {
    if (url.endsWith("/capabilities")) return { capabilities: [{
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    }] };
    const id = decodeURIComponent(url.split("/").at(-1) ?? "");
    return load(id);
  });
  return {
    context: { baseUrl: `https://studio.example/${name}`, http: { getJson, ...mutations } } as unknown as StudioEndpointContext,
    getJson,
    mutations
  };
}

function definition(id: string) {
  return { definition: { id, name: `Definition ${id}`, createdAt: "", lastModifiedAt: "", versionCount: 1 }, versions: [] };
}

function button(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === label) ?? null;
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function summary(overrides: Partial<WorkflowInstanceSummary> = {}): WorkflowInstanceSummary {
  return {
    workflowExecutionId: "workflow-execution-1",
    artifactId: "artifact-pinned",
    definitionId: "definition-1",
    definitionVersionId: "definition-version-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:pinned",
    status: "Running",
    createdAt: "2026-10-01T12:00:00Z",
    activityCount: 1,
    incidentCount: 1,
    ...overrides
  };
}

function incident(overrides: Partial<IncidentStateSummary> = {}): IncidentStateSummary {
  return {
    incidentId: "incident-1",
    workflowExecutionId: "workflow-execution-1",
    activityExecutionId: "activity-execution-1",
    executableNodeId: "exec-node",
    severity: "Error",
    status: "Open",
    failureType: "ActivityInputFailed",
    message: "Wrapper failure",
    createdAt: "2026-10-01T12:00:00Z",
    isBlocking: true,
    metadata: {},
    ...overrides
  };
}
