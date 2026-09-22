import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ActivityUpgradeWorkbenchPage } from "../ActivityUpgradeWorkbenchPage";
import type {
  ActivityUpgradeApplyResult,
  ActivityUpgradePlan
} from "../activityUpgradeTypes";
import { activityDefinitionsObservationEvent } from "../activityDefinitionObservability";
import { clearApiCapabilityCache } from "../api/capabilities";

describe("Activity upgrade workbench", () => {
  afterEach(() => {
    document.body.replaceChildren();
    window.history.replaceState({}, "", "/");
    window.sessionStorage?.removeItem("elsa.activity-upgrade-workbench.v1");
    clearApiCapabilityCache();
  });

  it("reviews the authoritative bottom-up plan, hands off publication, refreshes a successor, and finishes with durable receipts", async () => {
    const observations: Array<Record<string, unknown>> = [];
    const observe = (event: Event) => observations.push((event as CustomEvent).detail);
    window.addEventListener(activityDefinitionsObservationEvent, observe);
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityVersion&rootId=activity-owner-v1&fromVersion=dependency-v1");
    let planOneApplied = false;
    let planTwoApplied = false;
    const requests: Array<{ url: string; body: unknown }> = [];
    const context = endpoint({
      get(url) {
        if (url.includes("/upgrade-plans/plan-1")) return planOne(planOneApplied);
        if (url.includes("/upgrade-plans/plan-2")) return planTwo(planTwoApplied);
        throw new Error(`Unexpected GET ${url}`);
      },
      post(url, body) {
        requests.push({ url, body });
        if (url.endsWith("/upgrade-plans")) return planOne(false);
        if (url.endsWith("/plan-1/apply")) {
          planOneApplied = true;
          return childApplyResult();
        }
        if (url.endsWith("/plan-1/refresh")) return planTwo(false);
        if (url.endsWith("/plan-2/apply")) {
          planTwoApplied = true;
          return finalApplyResult();
        }
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    await waitForText(rendered.container, "ActivityVersion");
    expect(rendered.container.textContent).toContain("Immutable root · the plan must clone a draft");
    setInput(inputByLabel(rendered.container, "Replacement target version"), "dependency-v2");
    click(buttonByText(rendered.container, "Review authoritative plan"));

    await waitForText(rendered.container, "Selected dependency closure");
    expect(rendered.container.textContent).toContain("Bottom-up stage 1");
    expect(rendered.container.textContent).toContain("Clone Activity Definition Version");
    expect(rendered.container.textContent).toContain("Breaking · Major");
    expect(rendered.container.textContent).toContain("Expected revision 7");
    click(buttonByText(rendered.container, "Apply stage 1 atomically"));

    await waitForText(rendered.container, "Durable apply receipt");
    expect(rendered.container.querySelector(".au-receipts")?.getAttribute("role")).toBe("status");
    expect(rendered.container.querySelector(".au-receipts")?.getAttribute("aria-live")).toBe("polite");
    expect(rendered.container.querySelector(".au-handoff")?.getAttribute("role")).toBeNull();
    expect(rendered.container.querySelector(".au-handoff [role='status']")?.getAttribute("aria-live")).toBe("polite");
    expect(rendered.container.textContent).toContain("receipt-child");
    const childLink = rendered.container.querySelector<HTMLAnchorElement>('a[href*="activity-child-draft"]');
    expect(childLink?.getAttribute("href")).toContain("section=editor");
    expect(childLink?.getAttribute("href")).toContain("draft=activity-child-draft");
    expect(childLink?.getAttribute("href")).toContain("returnPlan=plan-1");

    const publishedVersion = inputByLabel(rendered.container, "Published version for activity-child-draft");
    setInput(publishedVersion, "activity-child-v2");
    click(buttonByText(rendered.container, "Refresh successor plan"));

    await waitForText(rendered.container, "Successor plan");
    expect(rendered.container.textContent).toContain("plan-2");
    expect(window.location.search).toContain("plan=plan-2");
    expect(rendered.container.querySelector('[aria-label="Published version for activity-child-draft"]')).toBeNull();
    expect(JSON.parse(window.sessionStorage.getItem("elsa.activity-upgrade-workbench.v1") ?? "{}").receipts).toEqual([
      { planId: "plan-1", receiptId: "receipt-child" }
    ]);
    click(buttonByText(rendered.container, "Apply stage 1 atomically"));

    await waitForText(rendered.container, "Upgrade complete");
    expect(rendered.container.textContent).toContain("receipt-final");
    expect(requests[0]).toMatchObject({
      body: {
        replacements: [{ fromVersionId: "dependency-v1", toVersionId: "dependency-v2" }],
        roots: [{ kind: "ActivityVersion", id: "activity-owner-v1" }]
      }
    });
    expect(requests.find(request => request.url.endsWith("/plan-1/refresh"))?.body).toEqual({
      publications: [{
        receiptId: "receipt-child",
        publishedDrafts: [{ draftId: "activity-child-draft", publishedVersionId: "activity-child-v2" }]
      }]
    });
    expect(observations).toEqual(expect.arrayContaining([
      { event: "upgrade-plan", surface: "upgrade-workbench", outcome: "pending" },
      { event: "upgrade-plan", surface: "upgrade-workbench", outcome: "ready" },
      { event: "upgrade-apply", surface: "upgrade-workbench", outcome: "pending" },
      { event: "upgrade-apply", surface: "upgrade-workbench", outcome: "waiting" },
      { event: "upgrade-apply", surface: "upgrade-workbench", outcome: "completed" }
    ]));
    expect(JSON.stringify(observations)).not.toMatch(/plan-1|plan-2|activity-child-draft|dependency-v/);
    await rendered.unmount();
    window.removeEventListener(activityDefinitionsObservationEvent, observe);
  });

  it("starts an explicitly seeded exact root instead of reopening an unrelated persisted plan", async () => {
    window.sessionStorage.setItem("elsa.activity-upgrade-workbench.v1", JSON.stringify({
      planId: "unrelated-plan",
      receipts: [],
      idempotencyByStage: {}
    }));
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=new-root");
    const context = endpoint({
      get(url) {
        throw new Error(`Unexpected GET ${url}`);
      },
      post(url) {
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    await waitForText(rendered.container, "new-root");
    expect(rendered.container.textContent).toContain("Mutable exact draft root");
    expect(rendered.container.textContent).not.toContain("unrelated-plan");
    expect(context.http.getJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reconciles a lost response through durable status without sending a second mutation", async () => {
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=workflow-draft&fromVersion=dependency-v1");
    let applied = false;
    let applyCalls = 0;
    let receiptReads = 0;
    const context = endpoint({
      get(url) {
        if (url.includes("/receipts/")) {
          receiptReads += 1;
          const receiptId = url.split("/").at(-1)!;
          applied = true;
          return appliedReceipt("plan-lost", "stage-parent", receiptId);
        }
        if (url.includes("/upgrade-plans/plan-lost")) return singleStagePlan("plan-lost", applied);
        throw new Error(`Unexpected GET ${url}`);
      },
      post(url) {
        if (url.endsWith("/upgrade-plans")) return singleStagePlan("plan-lost", false);
        if (url.endsWith("/plan-lost/apply")) {
          applyCalls += 1;
          throw new Error("connection ended after commit");
        }
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    setInput(inputByLabel(rendered.container, "Replacement target version"), "dependency-v2");
    click(buttonByText(rendered.container, "Review authoritative plan"));
    await waitForText(rendered.container, "Selected dependency closure");
    click(buttonByText(rendered.container, "Apply stage 1 atomically"));

    await waitForText(rendered.container, "Upgrade complete");
    expect(applyCalls).toBe(1);
    expect(receiptReads).toBe(1);
    expect(rendered.container.textContent).toContain("Durable apply receipt");
    await rendered.unmount();
  });

  it("retries only after a missing durable receipt and exposes a deterministic stale rejection", async () => {
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=workflow-draft&fromVersion=dependency-v1");
    let applyCalls = 0;
    let receiptReads = 0;
    const context = endpoint({
      get(url) {
        if (url.includes("/receipts/")) {
          receiptReads += 1;
          throw new StudioHttpError(404, "hidden receipt");
        }
        if (url.includes("/upgrade-plans/plan-retry")) return singleStagePlan("plan-retry", false);
        throw new Error(`Unexpected GET ${url}`);
      },
      post(url) {
        if (url.endsWith("/upgrade-plans")) return singleStagePlan("plan-retry", false);
        if (url.endsWith("/plan-retry/apply")) {
          applyCalls += 1;
          if (applyCalls === 1) throw new Error("connection ended before status");
          throw new StudioHttpError(409, "hidden stale identity", null, {
            errorCode: "activity.upgrade.stale-plan"
          });
        }
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    setInput(inputByLabel(rendered.container, "Replacement target version"), "dependency-v2");
    click(buttonByText(rendered.container, "Review authoritative plan"));
    await waitForText(rendered.container, "Selected dependency closure");
    click(buttonByText(rendered.container, "Apply stage 1 atomically"));
    await waitForText(rendered.container, "no receipt is visible");
    click(buttonByText(rendered.container, "Retry same idempotent stage"));

    await waitForText(rendered.container, "Reviewed evidence is stale");
    expect(rendered.container.textContent).toContain("No partial stage mutation was committed");
    expect(rendered.container.textContent).not.toContain("hidden stale identity");
    expect(applyCalls).toBe(2);
    expect(receiptReads).toBe(1);
    expect(buttonByText(rendered.container, "Apply stage 1 atomically")).toHaveProperty("disabled", true);
    await rendered.unmount();
  });

  it("fails closed when a saved plan is unavailable without disclosing hidden identities", async () => {
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?plan=hidden-plan");
    const context = endpoint({
      get() {
        throw new StudioHttpError(403, "secret plan for hidden-definition");
      },
      post(url) {
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    await waitForText(rendered.container, "unavailable for this account or address");
    expect(rendered.container.textContent).not.toContain("hidden-definition");
    expect(rendered.container.textContent).not.toContain("secret plan");
    expect(rendered.container.textContent).not.toContain("hidden-plan");
    await rendered.unmount();
  });

  it("preserves reviewed evidence when Apply is partially authorized and hides backend identities", async () => {
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=workflow-draft&fromVersion=dependency-v1");
    const context = endpoint({
      get(url) {
        if (url.includes("/upgrade-plans/plan-forbidden")) return singleStagePlan("plan-forbidden", false);
        throw new Error(`Unexpected GET ${url}`);
      },
      post(url) {
        if (url.endsWith("/upgrade-plans")) return singleStagePlan("plan-forbidden", false);
        if (url.endsWith("/plan-forbidden/apply")) {
          throw new StudioHttpError(403, "hidden owner activity-secret");
        }
        throw new Error(`Unexpected POST ${url}`);
      }
    });
    const rendered = renderWorkbench(context);

    setInput(inputByLabel(rendered.container, "Replacement target version"), "dependency-v2");
    click(buttonByText(rendered.container, "Review authoritative plan"));
    await waitForText(rendered.container, "Selected dependency closure");
    click(buttonByText(rendered.container, "Apply stage 1 atomically"));

    await waitForText(rendered.container, "unavailable for this account or exact plan");
    expect(rendered.container.textContent).toContain("Selected dependency closure");
    expect(rendered.container.textContent).not.toContain("activity-secret");
    expect(rendered.container.textContent).not.toContain("hidden owner");
    expect(rendered.container.textContent).not.toContain("Durable apply receipt");
    await rendered.unmount();
  });

  it("fails closed without a legacy fallback when the upgrade capability is unavailable", async () => {
    window.history.replaceState({}, "", "/workflows/activity-definitions/upgrades?rootKind=ActivityDraft&rootId=workflow-draft&fromVersion=dependency-v1");
    const context = {
      baseUrl: `test://activity-upgrade-unavailable-${Math.random()}`,
      http: {
        getJson: vi.fn(async () => ({
          capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [] }]
        })),
        postJson: vi.fn()
      }
    } as unknown as StudioEndpointContext;
    const rendered = renderWorkbench(context);

    setInput(inputByLabel(rendered.container, "Replacement target version"), "dependency-v2");
    click(buttonByText(rendered.container, "Review authoritative plan"));

    await waitForText(rendered.container, "does not advertise staged Activity Definition upgrades");
    expect(rendered.container.textContent).toContain("No fallback or unconfirmed plan is used");
    expect(rendered.container.textContent).not.toContain("Reviewed upgrade plan");
    expect(context.http.postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });
});

function renderWorkbench(context: StudioEndpointContext) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<ActivityUpgradeWorkbenchPage context={context} />));
  return {
    container,
    unmount: async () => flushSync(() => root.unmount())
  };
}

function endpoint(handlers: {
  get(url: string): unknown;
  post(url: string, body: unknown): unknown;
}): StudioEndpointContext {
  const links = [
    { rel: "activity-upgrade-plans", href: "design/activities/upgrade-plans" },
    { rel: "activity-upgrade-plan", href: "design/activities/upgrade-plans/{planId}", templated: true },
    { rel: "activity-upgrade-plan-apply", href: "design/activities/upgrade-plans/{planId}/apply", templated: true },
    { rel: "activity-upgrade-apply-receipt", href: "design/activities/upgrade-plans/{planId}/receipts/{receiptId}", templated: true },
    { rel: "activity-upgrade-plan-refresh", href: "design/activities/upgrade-plans/{planId}/refresh", templated: true }
  ];
  return {
    baseUrl: `test://activity-upgrade-workbench-${Math.random()}`,
    http: {
      getJson: vi.fn(async <T,>(url: string) => url === "/capabilities"
        ? ({ capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links }] } as T)
        : (handlers.get(url) as T)),
      postJson: vi.fn(async <T,>(url: string, body: unknown) => handlers.post(url, body) as T)
    } as unknown as StudioEndpointContext["http"]
  };
}

function planOne(applied: boolean): ActivityUpgradePlan {
  return {
    planId: "plan-1",
    createdAt: "2026-07-19T04:00:00Z",
    expiresAt: "2026-07-19T05:00:00Z",
    status: applied ? "AwaitingPublication" : "Ready",
    replacements: [{
      from: { definitionId: "dependency", versionId: "dependency-v1", version: "1.0.0", templateHash: "hash-1" },
      to: { definitionId: "dependency", versionId: "dependency-v2", version: "2.0.0", templateHash: "hash-2" }
    }],
    expectedSnapshots: [
      { kind: "ActivityVersion", id: "activity-owner-v1", definitionId: "activity-owner", revision: null, headVersionId: "activity-owner-v1" },
      { kind: "WorkflowDraft", id: "workflow-draft", definitionId: "workflow", revision: 7, headVersionId: "workflow-v3" }
    ],
    steps: [
      {
        stepId: "step-child",
        order: 10,
        target: { kind: "ActivityDraft", definitionId: "activity-child", sourceVersionId: "activity-owner-v1" },
        action: "CloneActivityVersion",
        dependsOnStepIds: [],
        replacements: [{ occurrenceId: "child-node", fromVersionId: "dependency-v1", toVersionId: "dependency-v2" }],
        expectedRevision: null,
        expectedDefinitionHeadVersionId: "activity-child-v1",
        resultingDiff: {
          compatibility: "Breaking",
          requiredBump: "Major",
          behaviorChanged: true,
          summary: { breaking: 1, additive: 0, nonBehavioral: 0, warnings: 0 }
        },
        diagnostics: [],
        stageId: "stage-child"
      },
      {
        stepId: "step-parent",
        order: 20,
        target: { kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 },
        action: "UpdateDraft",
        dependsOnStepIds: ["step-child"],
        replacements: [{ occurrenceId: "parent-node", fromVersionId: "activity-owner-v1", toVersionId: "activity-child-v2" }],
        expectedRevision: 7,
        expectedDefinitionHeadVersionId: "workflow-v3",
        resultingDiff: null,
        diagnostics: [],
        stageId: "stage-parent"
      }
    ],
    stages: [
      { stageId: "stage-child", order: 10, status: applied ? "Applied" : "Ready", stepIds: ["step-child"], dependsOnStageIds: [] },
      { stageId: "stage-parent", order: 20, status: applied ? "AwaitingPublication" : "Ready", stepIds: ["step-parent"], dependsOnStageIds: ["stage-child"] }
    ],
    binding: {
      roots: [{ kind: "ActivityVersion", id: "activity-owner-v1" }],
      includeTransitiveDependents: true,
      createDraftsForPublishedDependents: true,
      accessProfileFingerprint: "private-access-profile",
      selectedClosure: [
        { kind: "ActivityVersion", definitionId: "activity-owner", versionId: "activity-owner-v1", version: "1.0.0" },
        { kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 }
      ]
    },
    diagnostics: [],
    predecessorPlanId: null,
    successorPlanId: applied ? "plan-2" : null
  };
}

function planTwo(applied: boolean): ActivityUpgradePlan {
  const parent = planOne(true).steps[1];
  return {
    ...planOne(true),
    planId: "plan-2",
    status: applied ? "Applied" : "Ready",
    replacements: [
      ...planOne(true).replacements,
      {
        from: { definitionId: "activity-child", versionId: "activity-owner-v1", version: "1.0.0", templateHash: "hash-child-1" },
        to: { definitionId: "activity-child", versionId: "activity-child-v2", version: "2.0.0", templateHash: "hash-child-2" }
      }
    ],
    steps: [parent],
    stages: [{ stageId: "stage-parent", order: 10, status: applied ? "Applied" : "Ready", stepIds: ["step-parent"], dependsOnStageIds: [] }],
    predecessorPlanId: "plan-1",
    successorPlanId: null
  };
}

function childApplyResult(): ActivityUpgradeApplyResult {
  return {
    planId: "plan-1",
    status: "AwaitingPublication",
    appliedAt: "2026-07-19T04:10:00Z",
    drafts: [{ kind: "ActivityDraft", draftId: "activity-child-draft", definitionId: "activity-child", revision: 1, created: true, sourceVersionId: "activity-owner-v1" }],
    diagnostics: [],
    receiptId: "receipt-child",
    stageId: "stage-child",
    awaitingPublications: [{
      draftKind: "ActivityDraft",
      draftId: "activity-child-draft",
      definitionId: "activity-child",
      revision: 1,
      sourceVersionId: "activity-owner-v1",
      requiredByStageIds: ["stage-parent"]
    }]
  };
}

function finalApplyResult(): ActivityUpgradeApplyResult {
  return {
    planId: "plan-2",
    status: "Applied",
    appliedAt: "2026-07-19T04:20:00Z",
    drafts: [{ kind: "WorkflowDraft", draftId: "workflow-draft", definitionId: "workflow", revision: 8, created: false }],
    diagnostics: [],
    receiptId: "receipt-final",
    stageId: "stage-parent",
    awaitingPublications: []
  };
}

function singleStagePlan(planId: string, applied: boolean): ActivityUpgradePlan {
  const base = planTwo(applied);
  return {
    ...base,
    planId,
    status: applied ? "Applied" : "Ready",
    predecessorPlanId: null,
    successorPlanId: null,
    binding: {
      ...base.binding!,
      roots: [{ kind: "ActivityDraft", id: "workflow-draft" }],
      selectedClosure: [{ kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 }]
    }
  };
}

function appliedReceipt(planId: string, stageId: string, receiptId: string) {
  const result: ActivityUpgradeApplyResult = {
    ...finalApplyResult(),
    planId,
    stageId,
    receiptId
  };
  return {
    receiptId,
    planId,
    stageId,
    status: "Applied",
    createdAt: "2026-07-19T04:20:00Z",
    updatedAt: "2026-07-19T04:20:01Z",
    result,
    diagnostics: []
  };
}

function inputByLabel(container: HTMLElement, label: string) {
  const input = container.querySelector<HTMLInputElement>(`[aria-label="${label}"]`);
  if (!input) throw new Error(`Input '${label}' not found.`);
  return input;
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function setInput(element: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

async function waitForText(container: HTMLElement, text: string) {
  const deadline = Date.now() + 5_000;
  while (!container.textContent?.includes(text)) {
    if (Date.now() > deadline) throw new Error(`Timed out waiting for '${text}'. Current text: ${container.textContent}`);
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}
