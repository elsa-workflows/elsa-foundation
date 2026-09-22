import { describe, expect, it } from "vitest";
import {
  classifyBoundaryCursorProblem,
  emptyBoundaryEvidenceSnapshot,
  executionOccurrenceLabel,
  markBoundaryEvidenceStale,
  mergeBoundaryEvidencePage,
  runtimeEvidencePresentation
} from "../runtimeBoundaryInspectorModel";
import type {
  ActivityExecutionHierarchyItem,
  ActivityExecutionHierarchyPage,
  ActivityExecutionInspectionValueSnapshot
} from "../workflowTypes";

describe("runtime boundary inspector snapshot model", () => {
  it("keeps repeated executions separate and orders by execution sequence plus identity", () => {
    const first = mergeBoundaryEvidencePage(emptyBoundaryEvidenceSnapshot(), page(42, [
      item("repeat-b", 8, { iterationId: "iteration-2" }),
      item("repeat-a", 8, { iterationId: "iteration-1" })
    ], "next"));
    const second = mergeBoundaryEvidencePage(first, page(42, [
      item("retry", 11, {
        attempt: {
          attemptNumber: 2,
          firstAttemptActivityExecutionId: "repeat-a",
          previousAttemptActivityExecutionId: "repeat-a",
          totalAttempts: 2
        }
      })
    ]));

    expect(second.items.map(entry => entry.activityExecutionId)).toEqual([
      "repeat-a",
      "repeat-b",
      "retry"
    ]);
    expect(second.items).toHaveLength(3);
    expect(executionOccurrenceLabel(second.items[0]!)).toContain("iteration iteration-1");
    expect(executionOccurrenceLabel(second.items[2]!)).toContain("attempt 2");
  });

  it("never mixes pages across committed-through watermarks", () => {
    const current = mergeBoundaryEvidencePage(
      emptyBoundaryEvidenceSnapshot(),
      page(42, [item("preserved", 1)], "next")
    );
    const changed = mergeBoundaryEvidencePage(current, page(43, [item("must-not-append", 2)]));

    expect(changed.items.map(entry => entry.activityExecutionId)).toEqual(["preserved"]);
    expect(changed.committedThroughSequence).toBe(42);
    expect(changed.staleReason).toBe("watermark-changed");
    expect(changed.nextCursor).toBeNull();
  });

  it.each([
    ["activity.cursor.expired", "snapshot-expired"],
    ["activity.cursor.binding-mismatch", "unknown"]
  ] as const)(
    "classifies %s cursor recovery without discarding loaded evidence",
    (errorCode, expectedReason) => {
      const problem = classifyBoundaryCursorProblem({
        payload: {
          errorCode,
          detail: "Restart from the first page."
        }
      });
      const loaded = mergeBoundaryEvidencePage(
        emptyBoundaryEvidenceSnapshot(),
        page(42, [item("preserved", 1)], "next")
      );
      const stale = markBoundaryEvidenceStale(loaded, problem!);

      expect(problem).toMatchObject({ recoverable: true, reason: expectedReason });
      expect(stale.items.map(entry => entry.activityExecutionId)).toEqual(["preserved"]);
      expect(stale.nextCursor).toBeNull();
      expect(stale.staleMessage).toContain("Restart");
    }
  );
});

describe("selected runtime value evidence presentation", () => {
  it.each([
    [{ state: "captured", snapshot: { kind: "string", preview: "visible" } }, "captured"],
    [{ state: "captured", snapshot: { kind: "redacted", reason: "secret" } }, "redacted"],
    [{ state: "notCaptured", captureMode: "None" }, "not-captured"],
    [{ state: "captureFailed", failure: { code: "capture.failed", message: "Failed" } }, "capture-failed"],
    [{ state: "captured", snapshot: { kind: "payloadReference", referenceKind: "blob", referenceId: "ref" } }, "payload-reference"]
  ] as const)("distinguishes %s as %s", (partial, expected) => {
    expect(runtimeEvidencePresentation(snapshot(partial))).toBe(expected);
  });
});

function page(
  committedThroughSequence: number,
  items: ActivityExecutionHierarchyItem[],
  nextCursor: string | null = null
): ActivityExecutionHierarchyPage {
  return {
    root: {
      workflowExecutionId: "run",
      activityExecutionId: "boundary",
      executionScopeId: "scope",
      definitionVersionId: "definition-version",
      templateHash: "sha256:template"
    },
    committedThroughSequence,
    effectiveLimit: 100,
    items,
    nextCursor
  };
}

function item(
  activityExecutionId: string,
  executionSequence: number,
  overrides: Partial<ActivityExecutionHierarchyItem> = {}
): ActivityExecutionHierarchyItem {
  return {
    activityExecutionId,
    workflowExecutionId: "run",
    executableNodeId: `node-${activityExecutionId}`,
    authoredActivityId: `authored-${activityExecutionId}`,
    activityType: "Example.Activity",
    activityTypeVersion: "1.0.0",
    status: "Completed",
    executionSequence,
    scheduledAt: "2026-07-19T00:00:00Z",
    relativeDepth: 1,
    outcomeNames: [],
    bookmarkCount: 0,
    incidentCount: 0,
    blockingIncidentCount: 0,
    metadata: {},
    ...overrides
  };
}

function snapshot(
  partial: Partial<ActivityExecutionInspectionValueSnapshot>
): ActivityExecutionInspectionValueSnapshot {
  return {
    name: "Value",
    subject: "ActivityInput",
    captureMode: "DiagnosticSnapshot",
    capturedAt: "2026-07-19T00:00:00Z",
    captureReason: "Test evidence.",
    isSensitive: false,
    metadata: {},
    ...partial
  };
}
