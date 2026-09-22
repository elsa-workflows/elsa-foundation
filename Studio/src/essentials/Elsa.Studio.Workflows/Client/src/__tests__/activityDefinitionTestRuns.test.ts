import { describe, expect, it } from "vitest";
import type { StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityInputContract } from "../activityDefinitionTypes";
import type { ActivityDraftTestRunView } from "../api/publishing";
import {
  activityTestRunWorkbenchUrl,
  classifyActivityTestRun,
  isTerminalActivityTestRun,
  parseActivityTestRunInputs
} from "../activityDefinitionTestRuns";

describe("Activity Definition Test Runs", () => {
  it("keeps Absent, Present null, and Present value distinct and applies defaults only to Absent", () => {
    const defaulted = input("currency", { defaultValue: "EUR" });
    const nullable = input("comment", { nullable: true });
    const contributed = input("order", { alias: "Contoso.Order" });
    const required = input("required", { required: true });
    const editor: StudioWorkflowRunInputEditorContribution = {
      id: "order",
      supports: candidate => candidate.type.alias === "Contoso.Order",
      component: () => null,
      validate: () => undefined,
      serialize: ({ draft }) => ({ id: draft })
    };

    const parsed = parseActivityTestRunInputs(
      [defaulted, nullable, contributed, required],
      {
        currency: { presence: "Absent", draft: "" },
        comment: { presence: "Null", draft: "" },
        order: { presence: "Value", draft: "order-42" },
        required: { presence: "Absent", draft: "" }
      },
      [editor]);

    expect(parsed.inputs).toEqual({
      currency: { state: "Absent" },
      comment: { state: "Present", value: null },
      order: { state: "Present", value: { id: "order-42" } }
    });
    expect(parsed.errors).toEqual({
      required: "required is required and has no default."
    });
    expect(JSON.stringify(parsed.inputs)).not.toContain("EUR");
  });

  it("rejects Present null for a non-nullable contract input", () => {
    const parsed = parseActivityTestRunInputs(
      [input("count")],
      { count: { presence: "Null", draft: "" } });

    expect(parsed.inputs).toEqual({});
    expect(parsed.errors).toEqual({ count: "count does not allow null." });
  });

  it.each([
    ["Pending", false, "waiting"],
    ["Running", false, "running"],
    ["Suspended", false, "suspended"],
    ["Completed", true, "completed"],
    ["Faulted", true, "faulted"],
    ["Cancelled", true, "cancelled"],
    ["ValidationRejected", true, "validation-rejected"],
    ["DispatchRejected", true, "dispatch-rejected"],
    ["DispatchAmbiguous", false, "dispatch-ambiguous"]
  ] as const)("maps %s to a distinct stage", (status, terminal, stage) => {
    const classified = classifyActivityTestRun(view(status));
    expect(classified).toBe(stage);
    expect(isTerminalActivityTestRun(classified)).toBe(terminal);
  });

  it("keeps command acceptance distinct from scheduling reconciliation", () => {
    const accepted = view("DispatchAccepted");

    expect(classifyActivityTestRun(accepted)).toBe("accepted");
    expect(classifyActivityTestRun(accepted, true)).toBe("waiting");
  });

  it("distinguishes expired evidence from an expired Source Reference with retained or active Runtime Evidence", () => {
    const retained = view("Completed", {
      sourceReferenceExpired: true,
      sourceReferenceRetained: false,
      evidenceRetained: true
    });
    const active = view("Running", {
      sourceReferenceExpired: true,
      sourceReferenceRetained: false,
      evidenceRetained: true,
      runStillActive: true
    });
    const expired = view("Completed", {
      sourceReferenceExpired: true,
      sourceReferenceRetained: false,
      evidenceRetained: false
    });

    expect(classifyActivityTestRun(retained)).toBe("completed");
    expect(classifyActivityTestRun(active)).toBe("running");
    expect(classifyActivityTestRun(expired)).toBe("expired");
  });

  it("opens the existing Run workbench focused on the eventual outer activity execution", () => {
    const run = view("Running");
    run.workflowExecutionId = "workflow/execution";
    run.outerActivityExecutionId = "outer activity";

    expect(activityTestRunWorkbenchUrl(run))
      .toBe("/workflows/instances/workflow%2Fexecution?activityExecutionId=outer%20activity");
  });
});

function input(
  referenceKey: string,
  options: {
    alias?: string;
    nullable?: boolean;
    required?: boolean;
    defaultValue?: unknown;
  } = {}
): ActivityInputContract {
  return {
    referenceKey,
    name: referenceKey,
    displayName: referenceKey,
    description: null,
    category: null,
    order: 0,
    uiHint: null,
    uiSpecifications: null,
    type: { alias: options.alias ?? "String", collectionKind: "Single" },
    isRequired: options.required ?? false,
    isNullable: options.nullable ?? false,
    default: Object.prototype.hasOwnProperty.call(options, "defaultValue")
      ? { syntax: "Literal", value: options.defaultValue }
      : null,
    storageDriverKey: "elsa.json",
    durability: "Durable"
  };
}

function view(
  status: string,
  expiration: Partial<ActivityDraftTestRunView["expiration"]> = {}
): ActivityDraftTestRunView {
  return {
    testRunId: "test-run-1",
    draftId: "draft-1",
    draftRevision: 4,
    artifactId: "wrapper-1",
    sourceReferenceId: "source-1",
    workflowExecutionId: "workflow-1",
    outerActivityExecutionId: null,
    status,
    commandDispatchStatus: "Accepted",
    reason: null,
    failure: null,
    expiration: {
      sourceReferenceExpiresAt: "2026-07-19T12:30:00Z",
      sourceReferenceExpired: false,
      sourceReferenceRetained: true,
      evidenceRetention: "RetainedUntilRuntimeEvidenceDeletion",
      evidenceExpiresAt: null,
      evidenceRetained: false,
      runExpiresAt: null,
      runStillActive: false,
      receiptExpiresAt: "2026-07-26T12:00:00Z",
      ...expiration
    },
    cancellation: {
      capabilityAdvertised: true,
      available: false,
      status: "Terminal",
      reason: null
    },
    requestedAt: "2026-07-19T12:00:00Z",
    updatedAt: "2026-07-19T12:00:01Z"
  };
}
