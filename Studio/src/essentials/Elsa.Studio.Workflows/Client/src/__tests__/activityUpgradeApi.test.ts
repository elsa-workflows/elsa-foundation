import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  applyActivityUpgradeStage,
  createActivityUpgradePlan,
  getActivityUpgradePlan,
  getActivityUpgradeReceipt,
  refreshActivityUpgradePlan
} from "../api/activityUpgrades";

describe("Activity upgrade API contract", () => {
  it("uses only advertised capability links and exact plan, root, stage, publication, and receipt identities", async () => {
    const context = endpoint();
    const signal = new AbortController().signal;

    await createActivityUpgradePlan(context, {
      replacements: [{ fromVersionId: "version-old", toVersionId: "version-new" }],
      roots: [
        { kind: "ActivityDraft", id: "activity-draft" },
        { kind: "WorkflowVersion", id: "workflow-version" }
      ],
      includeTransitiveDependents: true,
      createDraftsForPublishedDependents: true
    }, signal);
    await getActivityUpgradePlan(context, "plan/one", signal);
    await applyActivityUpgradeStage(context, "plan/one", "stage/child", "operation/key", signal);
    await getActivityUpgradeReceipt(context, "plan/one", "receipt/one", signal);
    await refreshActivityUpgradePlan(context, "plan/one", [{
      receiptId: "receipt/one",
      publishedDrafts: [{ draftId: "activity-draft", publishedVersionId: "activity-version-2" }]
    }], signal);

    expect(context.http.postJson).toHaveBeenNthCalledWith(
      1,
      "/design/activities/upgrade-plans",
      {
        replacements: [{ fromVersionId: "version-old", toVersionId: "version-new" }],
        roots: [
          { kind: "ActivityDraft", id: "activity-draft" },
          { kind: "WorkflowVersion", id: "workflow-version" }
        ],
        includeTransitiveDependents: true,
        createDraftsForPublishedDependents: true
      },
      { signal }
    );
    expect(context.http.getJson).toHaveBeenNthCalledWith(
      2,
      "/design/activities/upgrade-plans/plan%2Fone",
      { signal }
    );
    expect(context.http.postJson).toHaveBeenNthCalledWith(
      2,
      "/design/activities/upgrade-plans/plan%2Fone/apply",
      { stageId: "stage/child", idempotencyKey: "operation/key" },
      { signal }
    );
    expect(context.http.getJson).toHaveBeenNthCalledWith(
      3,
      "/design/activities/upgrade-plans/plan%2Fone/receipts/receipt%2Fone",
      { signal }
    );
    expect(context.http.postJson).toHaveBeenNthCalledWith(
      3,
      "/design/activities/upgrade-plans/plan%2Fone/refresh",
      {
        publications: [{
          receiptId: "receipt/one",
          publishedDrafts: [{ draftId: "activity-draft", publishedVersionId: "activity-version-2" }]
        }]
      },
      { signal }
    );
  });
});

function endpoint(): StudioEndpointContext {
  const links = [
    { rel: "activity-upgrade-plans", href: "design/activities/upgrade-plans" },
    { rel: "activity-upgrade-plan", href: "design/activities/upgrade-plans/{planId}", templated: true },
    { rel: "activity-upgrade-plan-apply", href: "design/activities/upgrade-plans/{planId}/apply", templated: true },
    { rel: "activity-upgrade-apply-receipt", href: "design/activities/upgrade-plans/{planId}/receipts/{receiptId}", templated: true },
    { rel: "activity-upgrade-plan-refresh", href: "design/activities/upgrade-plans/{planId}/refresh", templated: true }
  ];
  return {
    baseUrl: `test://activity-upgrades-${Math.random()}`,
    http: {
      requestJson: vi.fn(async <T,>() => ({} as T)),
      getJson: vi.fn(async <T,>(url: string) => url === "/capabilities"
        ? ({ capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links }] } as T)
        : ({} as T)),
      postJson: vi.fn(async <T,>() => ({} as T)),
      putJson: vi.fn(async <T,>() => ({} as T)),
      deleteJson: vi.fn(async <T,>() => ({} as T)),
      postForm: vi.fn(async <T,>() => ({} as T))
    } as unknown as StudioEndpointContext["http"]
  };
}
