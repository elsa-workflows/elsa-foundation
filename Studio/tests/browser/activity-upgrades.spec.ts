import { expect, test, type Page, type Route } from "@playwright/test";
import type {
  ActivityUpgradeApplyReceipt,
  ActivityUpgradeApplyResult,
  ActivityUpgradePlan
} from "../../src/Elsa.Studio.Workflows/Client/src/activityUpgradeTypes";

test("broad upgrades select only explicit roots, show the authoritative closure, and preserve atomic-failure evidence", async ({ page }) => {
  const requests: unknown[] = [];
  await mockUpgradeApi(page, {
    plan(planId) {
      return failurePlan(planId);
    },
    create(body) {
      requests.push(body);
      return failurePlan("plan-failure");
    },
    apply() {
      return {
        status: 409,
        body: {
          title: "Stale activity upgrade plan",
          detail: "hidden workflow-owner identity",
          errorCode: "activity.upgrade.stale-plan"
        }
      };
    }
  });

  await page.setViewportSize({ width: 360, height: 900 });
  await page.goto("/workflows/activity-definitions/upgrades?rootKind=ActivityVersion&rootId=activity-owner-v1&fromVersion=dependency-v1");
  await expect(page.getByText("Immutable root · the plan must clone a draft")).toBeVisible();
  await expect(page.locator(".au-root-list > li")).toHaveCount(1);
  await page.getByLabel("Root kind").selectOption("WorkflowDraft");
  await page.getByLabel("Exact root identity").fill("workflow-draft");
  await page.getByRole("button", { name: "Add exact root" }).click();
  await expect(page.locator(".au-root-list > li")).toHaveCount(2);
  await page.getByLabel("Replacement target version").fill("dependency-v2");
  await page.getByRole("button", { name: "Review authoritative plan" }).click();

  await expect(page.getByRole("heading", { name: "Selected dependency closure" })).toBeVisible();
  await expect(page.getByText("Bottom-up stage 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Expected revision 7")).toBeVisible();
  await expect(page.getByText("Breaking · Major")).toBeVisible();
  expect(requests).toEqual([expect.objectContaining({
    roots: [
      { kind: "ActivityVersion", id: "activity-owner-v1" },
      { kind: "WorkflowDraft", id: "workflow-draft" }
    ]
  })]);

  await page.getByRole("button", { name: "Apply stage 1 atomically" }).click();
  await expect(page.getByText("Reviewed evidence is stale")).toBeVisible();
  await expect(page.getByText("No partial stage mutation was committed")).toBeVisible();
  await expect(page.getByText("hidden workflow-owner identity")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Selected dependency closure" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Durable apply receipt/ })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Apply stage 1 atomically" })).toBeDisabled();
  expect(await overflowOffenders(page)).toEqual([]);
});

test("a lost child-stage response reconciles once, survives reload, hands off exact publication, refreshes a successor, and completes", async ({ page }) => {
  const state = {
    childApplied: false,
    finalApplied: false,
    childApplyBodies: [] as Array<{ stageId: string; idempotencyKey: string }>,
    finalApplyBodies: [] as Array<{ stageId: string; idempotencyKey: string }>,
    refreshBodies: [] as unknown[]
  };
  await mockUpgradeApi(page, {
    plan(planId) {
      if (planId === "plan-1") return stagedPlan(state.childApplied);
      if (planId === "plan-2") return successorPlan(state.finalApplied);
      throw new Error(`Unexpected plan ${planId}`);
    },
    create() {
      return stagedPlan(false);
    },
    apply(planId, body) {
      if (planId === "plan-1") {
        state.childApplyBodies.push(body);
        return {
          status: 500,
          body: { title: "Apply response lost", detail: "outcome not confirmed" }
        };
      }
      state.finalApplyBodies.push(body);
      state.finalApplied = true;
      return finalResult();
    },
    receipt(planId, receiptId) {
      if (planId !== "plan-1") throw new Error(`Unexpected receipt owner ${planId}`);
      state.childApplied = true;
      return appliedReceipt(planId, receiptId, childResult(receiptId));
    },
    refresh(planId, body) {
      expect(planId).toBe("plan-1");
      state.refreshBodies.push(body);
      return successorPlan(false);
    }
  });

  await page.goto("/workflows/activity-definitions/upgrades?rootKind=ActivityVersion&rootId=activity-owner-v1&fromVersion=dependency-v1&toVersion=dependency-v2");
  await page.getByRole("button", { name: "Review authoritative plan" }).click();
  await page.getByRole("button", { name: "Apply stage 1 atomically" }).click();

  await expect(page.getByRole("heading", { name: /Durable apply receipt/ })).toBeVisible();
  expect(state.childApplyBodies).toHaveLength(1);
  const handoff = page.getByRole("heading", { name: "Awaiting separately reviewed publication" }).locator("xpath=ancestor::section[1]");
  await expect(handoff).toBeVisible();
  const childLink = handoff.getByRole("link", { name: "Open next required draft" });
  await expect(childLink).toHaveAttribute("href", /section=editor.*draft=activity-child-draft.*returnPlan=plan-1/);

  await page.reload();
  await expect(page.getByRole("heading", { name: "Awaiting separately reviewed publication" })).toBeVisible();
  expect(state.childApplyBodies).toHaveLength(1);
  await page.getByRole("link", { name: "Open next required draft" }).click();
  await expect(page).toHaveURL(/section=editor.*draft=activity-child-draft.*returnPlan=plan-1/);
  await expect(page.getByRole("heading", { name: "Activity draft unavailable" })).toBeVisible();
  await page.getByRole("button", { name: "Activity Definition" }).click();
  await expect(page).toHaveURL(/activity-definitions\/upgrades\?plan=plan-1/);
  await expect(page.getByRole("heading", { name: "Awaiting separately reviewed publication" })).toBeVisible();
  await page.getByLabel("Published version for activity-child-draft").fill("activity-child-v2");
  await page.getByRole("button", { name: "Refresh successor plan" }).click();

  await expect(page.getByText("Successor plan")).toBeVisible();
  await expect(page).toHaveURL(/plan=plan-2/);
  await expect(page.getByRole("heading", { name: "Awaiting separately reviewed publication" })).toHaveCount(0);
  await expect(page.getByText(/^activity-upgrade-receipt-/)).toBeVisible();
  expect(state.refreshBodies).toEqual([{
    publications: [{
      receiptId: expect.stringMatching(/^activity-upgrade-receipt-/),
      publishedDrafts: [{ draftId: "activity-child-draft", publishedVersionId: "activity-child-v2" }]
    }]
  }]);

  await page.getByRole("button", { name: "Apply stage 1 atomically" }).click();
  await expect(page.getByText("Upgrade complete")).toBeVisible();
  await expect(page.getByText("receipt-final")).toBeVisible();
  expect(state.finalApplyBodies).toHaveLength(1);
});

test("an absent durable receipt allows one idempotent retry with the same key", async ({ page }) => {
  const state = {
    applied: false,
    applyBodies: [] as Array<{ stageId: string; idempotencyKey: string }>,
    receiptReads: 0
  };
  await mockUpgradeApi(page, {
    plan(planId) {
      return retryPlan(planId, state.applied);
    },
    create() {
      return retryPlan("plan-retry", false);
    },
    apply(_planId, body) {
      state.applyBodies.push(body);
      if (state.applyBodies.length === 1) {
        return {
          status: 500,
          body: { title: "Connection interrupted", detail: "outcome unknown" }
        };
      }
      state.applied = true;
      return retryResult();
    },
    receipt() {
      state.receiptReads += 1;
      return { status: 404, body: { title: "Receipt not found" } };
    }
  });

  await page.goto("/workflows/activity-definitions/upgrades?rootKind=WorkflowDraft&rootId=workflow-draft&fromVersion=dependency-v1&toVersion=dependency-v2");
  await page.getByRole("button", { name: "Review authoritative plan" }).click();
  await page.getByRole("button", { name: "Apply stage 1 atomically" }).click();
  await expect(page.getByText(/no receipt is visible/i)).toBeVisible();
  await page.getByRole("button", { name: "Retry same idempotent stage" }).click();

  await expect(page.getByText("Upgrade complete")).toBeVisible();
  expect(state.receiptReads).toBe(1);
  expect(state.applyBodies).toHaveLength(2);
  expect(state.applyBodies[1].idempotencyKey).toBe(state.applyBodies[0].idempotencyKey);
});

type UpgradeReply<T> = T | { status: number; body: unknown };

interface UpgradeApi {
  plan(planId: string): ActivityUpgradePlan;
  create(body: unknown): ActivityUpgradePlan;
  apply(planId: string, body: { stageId: string; idempotencyKey: string }): UpgradeReply<ActivityUpgradeApplyResult>;
  receipt?(planId: string, receiptId: string): UpgradeReply<ActivityUpgradeApplyReceipt>;
  refresh?(planId: string, body: unknown): ActivityUpgradePlan;
}

async function mockUpgradeApi(page: Page, api: UpgradeApi) {
  await page.route("**/capabilities", route => json(route, {
    capabilities: [{
      id: "elsa.api.activity-design",
      contractVersion: "1",
      links: [
        { rel: "activity-upgrade-plans", href: "design/activities/upgrade-plans" },
        { rel: "activity-upgrade-plan", href: "design/activities/upgrade-plans/{planId}", templated: true },
        { rel: "activity-upgrade-plan-apply", href: "design/activities/upgrade-plans/{planId}/apply", templated: true },
        { rel: "activity-upgrade-apply-receipt", href: "design/activities/upgrade-plans/{planId}/receipts/{receiptId}", templated: true },
        { rel: "activity-upgrade-plan-refresh", href: "design/activities/upgrade-plans/{planId}/refresh", templated: true }
      ]
    }]
  }));
  await page.route("**/design/activities/upgrade-plans**", async route => {
    const request = route.request();
    const url = new URL(request.url());
    const segments = url.pathname.split("/").filter(Boolean);
    const planIndex = segments.indexOf("upgrade-plans");
    const planId = segments[planIndex + 1];
    const tail = segments[planIndex + 2];
    if (request.method() === "POST" && !planId) return json(route, api.create(request.postDataJSON()));
    if (request.method() === "GET" && planId && !tail) return json(route, api.plan(planId));
    if (request.method() === "POST" && tail === "apply") {
      return reply(route, api.apply(planId, request.postDataJSON()));
    }
    if (request.method() === "GET" && tail === "receipts") {
      return reply(route, api.receipt?.(planId, segments[planIndex + 3]) ?? {
        status: 404,
        body: { title: "Receipt not found" }
      });
    }
    if (request.method() === "POST" && tail === "refresh" && api.refresh) {
      return json(route, api.refresh(planId, request.postDataJSON()));
    }
    return route.abort();
  });
}

function stagedPlan(childApplied: boolean): ActivityUpgradePlan {
  const base = planBase("plan-1", childApplied ? "AwaitingPublication" : "Ready");
  return {
    ...base,
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
      workflowStep("stage-parent", 20)
    ],
    stages: [
      { stageId: "stage-child", order: 10, status: childApplied ? "Applied" : "Ready", stepIds: ["step-child"], dependsOnStageIds: [] },
      { stageId: "stage-parent", order: 20, status: childApplied ? "AwaitingPublication" : "Ready", stepIds: ["step-parent"], dependsOnStageIds: ["stage-child"] }
    ]
  };
}

function successorPlan(applied: boolean): ActivityUpgradePlan {
  const base = planBase("plan-2", applied ? "Applied" : "Ready");
  return {
    ...base,
    replacements: [
      ...base.replacements,
      {
        from: { definitionId: "activity-child", versionId: "activity-owner-v1", version: "1.0.0", templateHash: "hash-child-1" },
        to: { definitionId: "activity-child", versionId: "activity-child-v2", version: "2.0.0", templateHash: "hash-child-2" }
      }
    ],
    steps: [workflowStep("stage-parent", 10)],
    stages: [{ stageId: "stage-parent", order: 10, status: applied ? "Applied" : "Ready", stepIds: ["step-parent"], dependsOnStageIds: [] }],
    predecessorPlanId: "plan-1"
  };
}

function failurePlan(planId: string) {
  const plan = retryPlan(planId, false);
  return {
    ...plan,
    binding: {
      ...plan.binding!,
      roots: [
        { kind: "ActivityVersion" as const, id: "activity-owner-v1" },
        { kind: "WorkflowDraft" as const, id: "workflow-draft" }
      ],
      selectedClosure: [
        { kind: "ActivityVersion", definitionId: "activity-owner", versionId: "activity-owner-v1", version: "1.0.0" },
        { kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 }
      ]
    }
  };
}

function retryPlan(planId: string, applied: boolean): ActivityUpgradePlan {
  const base = planBase(planId, applied ? "Applied" : "Ready");
  return {
    ...base,
    steps: [workflowStep("stage-parent", 10)],
    stages: [{ stageId: "stage-parent", order: 10, status: applied ? "Applied" : "Ready", stepIds: ["step-parent"], dependsOnStageIds: [] }]
  };
}

function planBase(planId: string, status: string): ActivityUpgradePlan {
  return {
    planId,
    createdAt: "2026-07-19T04:00:00Z",
    expiresAt: "2026-07-19T05:00:00Z",
    status,
    replacements: [{
      from: { definitionId: "dependency", versionId: "dependency-v1", version: "1.0.0", templateHash: "hash-1" },
      to: { definitionId: "dependency", versionId: "dependency-v2", version: "2.0.0", templateHash: "hash-2" }
    }],
    expectedSnapshots: [
      { kind: "WorkflowDraft", id: "workflow-draft", definitionId: "workflow", revision: 7, headVersionId: "workflow-v3" }
    ],
    steps: [],
    stages: [],
    binding: {
      roots: [{ kind: "WorkflowDraft", id: "workflow-draft" }],
      includeTransitiveDependents: true,
      createDraftsForPublishedDependents: true,
      accessProfileFingerprint: "browser-private",
      selectedClosure: [{ kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 }]
    },
    diagnostics: [],
    predecessorPlanId: null,
    successorPlanId: null
  };
}

function workflowStep(stageId: string, order: number) {
  return {
    stepId: "step-parent",
    order,
    target: { kind: "WorkflowDraft", definitionId: "workflow", draftId: "workflow-draft", revision: 7 },
    action: "UpdateDraft",
    dependsOnStepIds: [],
    replacements: [{ occurrenceId: "parent-node", fromVersionId: "dependency-v1", toVersionId: "dependency-v2" }],
    expectedRevision: 7,
    expectedDefinitionHeadVersionId: "workflow-v3",
    resultingDiff: {
      compatibility: "Breaking",
      requiredBump: "Major",
      behaviorChanged: true,
      summary: { breaking: 1, additive: 0, nonBehavioral: 0, warnings: 0 }
    },
    diagnostics: [],
    stageId
  };
}

function childResult(receiptId: string): ActivityUpgradeApplyResult {
  return {
    planId: "plan-1",
    status: "AwaitingPublication",
    appliedAt: "2026-07-19T04:10:00Z",
    drafts: [{ kind: "ActivityDraft", draftId: "activity-child-draft", definitionId: "activity-child", revision: 1, created: true, sourceVersionId: "activity-owner-v1" }],
    diagnostics: [],
    receiptId,
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

function finalResult(): ActivityUpgradeApplyResult {
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

function retryResult(): ActivityUpgradeApplyResult {
  return {
    ...finalResult(),
    planId: "plan-retry",
    receiptId: "receipt-retry"
  };
}

function appliedReceipt(planId: string, receiptId: string, result: ActivityUpgradeApplyResult): ActivityUpgradeApplyReceipt {
  return {
    receiptId,
    planId,
    stageId: result.stageId!,
    status: "Applied",
    createdAt: result.appliedAt,
    updatedAt: result.appliedAt,
    result,
    diagnostics: []
  };
}

async function reply<T>(route: Route, value: UpgradeReply<T>) {
  if (isHttpReply(value)) return json(route, value.body, value.status);
  return json(route, value);
}

function isHttpReply<T>(value: UpgradeReply<T>): value is { status: number; body: unknown } {
  return typeof value === "object" && value !== null && "status" in value && "body" in value;
}

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: status >= 400 ? "application/problem+json" : "application/json",
    body: JSON.stringify(body)
  });
}

async function overflowOffenders(page: Page) {
  return page.evaluate(() =>
    [...document.querySelectorAll("*")]
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return rect.right > document.documentElement.clientWidth + 1 || rect.left < -1;
      })
      .slice(0, 8)
      .map(element => ({
        tag: element.tagName,
        className: element.className,
        text: element.textContent?.slice(0, 60)
      }))
  );
}
