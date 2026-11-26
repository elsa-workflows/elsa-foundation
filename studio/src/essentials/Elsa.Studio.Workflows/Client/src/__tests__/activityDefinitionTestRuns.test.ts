import { describe, expect, it } from "vitest";
import type { StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityInputContract } from "../activityDefinitionTypes";
import type { ActivityDraftTestRunView } from "../api/publishing";
import {
  activityTestRunActivityUrl,
  activityTestRunExecutableUrl,
  activityTestRunWorkflowUrl,
  activityTestRunWorkbenchUrl,
  classifyActivityTestRun,
  isTerminalActivityTestRun,
  parseActivityTestRunInputs,
  shortenActivityTestRunIdentifier
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

  it.each(["Running", "DispatchAccepted", "Pending"])(
    "treats AcceptedButFaulted command dispatch as terminal when the run status is %s",
    status => {
      const run = view(status);
      run.commandDispatchStatus = "AcceptedButFaulted";

      const classified = classifyActivityTestRun(run);
      expect(classified).toBe("faulted");
      expect(isTerminalActivityTestRun(classified)).toBe(true);
    });

  it("matches AcceptedButFaulted dispatch status without regard to case", () => {
    const run = view("Running");
    run.commandDispatchStatus = "acceptedbutfaulted";

    expect(classifyActivityTestRun(run)).toBe("faulted");
  });

  it.each([
    ["Completed", "completed"],
    ["Cancelled", "cancelled"],
    ["ValidationRejected", "validation-rejected"],
    ["DispatchRejected", "dispatch-rejected"],
    ["Faulted", "faulted"]
  ] as const)("keeps terminal run status %s authoritative over AcceptedButFaulted", (status, stage) => {
    const run = view(status);
    run.commandDispatchStatus = "AcceptedButFaulted";

    expect(classifyActivityTestRun(run)).toBe(stage);
  });

  it("distinguishes expired evidence from an expired Source Reference with retained or active Run data", () => {
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
    expired.commandDispatchStatus = "AcceptedButFaulted";

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

  it("links recorded executions and retained executable references to their workbenches", () => {
    const run = view("Completed");
    run.workflowExecutionId = "workflow/execution";
    run.outerActivityExecutionId = "outer activity";
    run.artifactId = "wrapper/artifact";
    run.sourceReferenceId = "source/reference";

    expect(activityTestRunWorkflowUrl(run)).toBe("/workflows/instances/workflow%2Fexecution");
    expect(activityTestRunActivityUrl(run)).toBe("/workflows/instances/workflow%2Fexecution?activityExecutionId=outer%20activity");
    expect(activityTestRunExecutableUrl(run)).toBe("/workflows/executables/wrapper%2Fartifact?ref=source%2Freference");
  });

  it("keeps rejected, expired, and unretained reference identities from linking to unavailable records", () => {
    const rejected = view("DispatchRejected");
    rejected.artifactId = "rejected-artifact";
    rejected.sourceReferenceId = "rejected-reference";
    const expired = view("Completed", {
      sourceReferenceExpired: true,
      sourceReferenceRetained: false,
      evidenceRetained: false
    });
    expired.artifactId = "expired-artifact";
    expired.sourceReferenceId = "expired-reference";
    const referenceExpiredButEvidenceRetained = view("Completed", {
      sourceReferenceExpired: true,
      sourceReferenceRetained: false,
      evidenceRetained: true
    });
    referenceExpiredButEvidenceRetained.artifactId = "retained-evidence-artifact";
    referenceExpiredButEvidenceRetained.sourceReferenceId = "expired-reference";
    const sourceReferenceUnavailable = view("Completed");
    sourceReferenceUnavailable.artifactId = "wrapper-without-reference";
    sourceReferenceUnavailable.sourceReferenceId = null;

    expect(activityTestRunWorkflowUrl(rejected)).toBeNull();
    expect(activityTestRunExecutableUrl(rejected)).toBeNull();
    expect(activityTestRunWorkflowUrl(expired)).toBeNull();
    expect(activityTestRunExecutableUrl(expired)).toBeNull();
    expect(activityTestRunExecutableUrl(referenceExpiredButEvidenceRetained)).toBeNull();
    expect(activityTestRunExecutableUrl(sourceReferenceUnavailable)).toBeNull();
  });

  it("shortens long identities without changing the exact copy value", () => {
    const identity = `activity-test-run-${"a".repeat(64)}`;

    expect(shortenActivityTestRunIdentifier("short-id")).toBe("short-id");
    expect(shortenActivityTestRunIdentifier(identity, 32)).toBe(`${identity.slice(0, 16)}…${identity.slice(-15)}`);
    expect(identity).toBe(`activity-test-run-${"a".repeat(64)}`);
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
