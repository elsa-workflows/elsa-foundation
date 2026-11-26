import { describe, expect, it } from "vitest";
import {
  addExactUpgradeRoot,
  buildActivityUpgradePlanRequest,
  createActivityUpgradeReceiptId,
  createActivityUpgradeDraft
} from "../activityUpgradeModel";

describe("Activity upgrade workbench model", () => {
  it("adds only explicit exact roots and never infers unseen rows or duplicate identities", () => {
    const initial = createActivityUpgradeDraft({
      rootKind: "ActivityVersion",
      rootId: "activity-version-root",
      fromVersionId: "dependency-v1"
    });
    const withWorkflow = addExactUpgradeRoot(initial, {
      kind: "WorkflowDraft",
      id: "workflow-draft-root"
    });
    const duplicate = addExactUpgradeRoot(withWorkflow, {
      kind: "ActivityVersion",
      id: "activity-version-root"
    });
    const request = buildActivityUpgradePlanRequest({
      ...duplicate,
      toVersionId: "dependency-v2",
      createDraftsForPublishedDependents: true
    });

    expect(request).toEqual({
      replacements: [{ fromVersionId: "dependency-v1", toVersionId: "dependency-v2" }],
      roots: [
        { kind: "ActivityVersion", id: "activity-version-root" },
        { kind: "WorkflowDraft", id: "workflow-draft-root" }
      ],
      includeTransitiveDependents: true,
      createDraftsForPublishedDependents: true
    });
  });

  it("derives the durable receipt identity before Apply so a lost response can be reconciled without retrying the mutation", async () => {
    await expect(createActivityUpgradeReceiptId("plan-1", "operation-1")).resolves.toBe(
      "activity-upgrade-receipt-df14e157bd36347a6267271b83a3eaeb230372a4d15913957859efb8f8343ddf"
    );
  });
});
