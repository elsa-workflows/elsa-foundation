import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionTestRunDialog, type PreparedActivityTestRunRevision } from "../ActivityDefinitionTestRunDialog";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";
import type { ActivityDraftTestRunView } from "../api/publishing";
import type { ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot } from "../workflowTypes";

const api = vi.hoisted(() => ({
  startActivityVersionTestRun: vi.fn(),
  getActivityExecutionInspection: vi.fn()
}));

vi.mock("../api/publishing", async importOriginal => ({
  ...await importOriginal<typeof import("../api/publishing")>(),
  startActivityVersionTestRun: api.startActivityVersionTestRun
}));

vi.mock("../api/runtime", async importOriginal => ({
  ...await importOriginal<typeof import("../api/runtime")>(),
  getActivityExecutionInspection: api.getActivityExecutionInspection
}));

afterEach(() => {
  document.body.replaceChildren();
});

describe("ActivityDefinitionTestRunDialog", () => {
  it("prepares the exact revision once under React Strict Mode", async () => {
    const prepared: PreparedActivityTestRunRevision = {
      revision: 5,
      validation: {
        draftId: "activity-draft-1",
        revision: 5,
        isValid: true,
        validatedAt: "2026-07-20T00:00:00Z",
        diagnostics: []
      }
    };
    const prepareExactRevision = vi.fn(async (onPhase: (phase: "saving" | "validating") => void) => {
      onPhase("validating");
      return prepared;
    });
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);

    flushSync(() => root.render(
      <React.StrictMode>
        <ActivityDefinitionTestRunDialog
          context={{} as StudioEndpointContext}
          draft={draft()}
          definitionLabel="Greeting"
          inputEditors={[]}
          expressionEditors={[]}
          prepareExactRevision={prepareExactRevision}
          onFocusDiagnostic={vi.fn()}
          onClose={vi.fn()}
          onOpenRun={vi.fn()}
        />
      </React.StrictMode>
    ));

    await waitFor(() => expect(document.body.textContent).toContain("Inputs"));
    expect(document.body.textContent).toContain("Run test");
    expect(document.body.textContent).not.toContain("Revision 5 validated");
    expect(prepareExactRevision).toHaveBeenCalledTimes(1);

    flushSync(() => root.unmount());
  });

  it("summarises a completed run and lists each recorded input once", async () => {
    api.startActivityVersionTestRun.mockResolvedValue(completedRun());
    api.getActivityExecutionInspection.mockResolvedValue(inspection([
      snapshot("ActivityOutput", "greeting", "Hello, Jack Bauer!", "2026-07-20T00:00:02Z"),
      snapshot("ActivityInput", "FirstName", "Jack", "2026-07-20T00:00:01Z", "first-name"),
      snapshot("ActivityInput", "LastName", "Bauer", "2026-07-20T00:00:01Z", "last-name"),
      snapshot("ActivityInput", "FirstName", "Jack", "2026-07-20T00:00:02Z", "first-name"),
      snapshot("ActivityInput", "LastName", "Bauer", "2026-07-20T00:00:02Z", "last-name")
    ]));
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);

    flushSync(() => root.render(
      <ActivityDefinitionTestRunDialog
        context={{} as StudioEndpointContext}
        draft={{ ...draft(), status: "published" }}
        sourceVersion={{ versionId: "version-1", version: "1.1.0", publishedAt: "2026-07-20T00:00:00Z" }}
        definitionLabel="Greeter"
        inputEditors={[]}
        expressionEditors={[]}
        prepareExactRevision={vi.fn()}
        onFocusDiagnostic={vi.fn()}
        onClose={vi.fn()}
        onOpenRun={vi.fn()}
      />
    ));

    await waitFor(() => expect(buttonByText("Run test")).toBeTruthy());
    flushSync(() => buttonByText("Run test").click());

    await waitFor(() => expect(document.body.querySelector("[aria-label='Outcome']")?.textContent).toContain("Done"));
    const result = document.body.querySelector("[aria-label='Test run result']")!;
    expect(result.querySelector(".ad-test-run-stage")?.textContent).toBe("Completed");
    expect(result.querySelector("[aria-label='Outputs']")?.textContent).toContain("Hello, Jack Bauer!");
    expect(result.querySelector(".ad-selected-record")).toBeNull();
    expect(buttonByText("Run again")).toBeTruthy();

    const details = result.querySelector<HTMLDetailsElement>("details.ad-test-run-details")!;
    flushSync(() => {
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await waitFor(() => expect(result.querySelector("[aria-label='Recorded inputs']")).not.toBeNull());
    const recordedNames = [...result.querySelectorAll("[aria-label='Recorded inputs'] .wf-runtime-input header strong")].map(item => item.textContent);
    expect(recordedNames).toEqual(["FirstName", "LastName"]);

    flushSync(() => root.unmount());
  });

  it("renders required input validation errors inside StudioField for omitted and value modes", async () => {
    api.startActivityVersionTestRun.mockClear();
    const requiredInput = (referenceKey: string, name: string, alias = "String") => ({
      referenceKey,
      name,
      displayName: name,
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias, collectionKind: "Single" as const },
      isRequired: true,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Durable" as const
    });
    const baseDraft = draft();
    const draftWithRequiredInputs: ActivityDefinitionDraftView = {
      ...baseDraft,
      status: "published",
      contract: {
        ...baseDraft.contract,
        inputs: [
          requiredInput("first-name", "FirstName", "String"),
          requiredInput("attempts", "Attempts", "Int32")
        ]
      }
    };
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);

    flushSync(() => root.render(
      <ActivityDefinitionTestRunDialog
        context={{} as StudioEndpointContext}
        draft={draftWithRequiredInputs}
        sourceVersion={{ versionId: "version-1", version: "2.0.0", publishedAt: "2026-07-20T00:00:00Z" }}
        definitionLabel="Greeter"
        inputEditors={[]}
        expressionEditors={[]}
        prepareExactRevision={vi.fn()}
        onFocusDiagnostic={vi.fn()}
        onClose={vi.fn()}
        onOpenRun={vi.fn()}
      />
    ));

    await waitFor(() => expect(buttonByText("Run test")).toBeTruthy());
    flushSync(() => buttonByText("Run test").click());

    expect(api.startActivityVersionTestRun).not.toHaveBeenCalled();
    expect(document.body.querySelector(".ad-test-run-input-error")).toBeNull();

    const firstSelect = document.body.querySelector<HTMLSelectElement>("select[aria-label='FirstName']")!;
    const firstField = firstSelect.closest(".studio-field")!;
    expect(firstField.getAttribute("data-invalid")).toBe("true");
    expect(firstSelect.getAttribute("aria-invalid")).toBe("true");
    const firstError = firstField.querySelector(".studio-field-error[role='alert']")!;
    expect(firstError.textContent).toBe("FirstName is required and has no default.");
    expect(firstSelect.getAttribute("aria-describedby")).toContain(firstError.id);

    const attemptsSelect = document.body.querySelector<HTMLSelectElement>("select[aria-label='Attempts']")!;
    const attemptsField = attemptsSelect.closest(".studio-field")!;
    flushSync(() => {
      attemptsSelect.value = "Value";
      attemptsSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(attemptsField.getAttribute("data-invalid")).toBe("false");

    flushSync(() => buttonByText("Run test").click());
    expect(api.startActivityVersionTestRun).not.toHaveBeenCalled();
    expect(attemptsField.getAttribute("data-invalid")).toBe("false");

    const attemptsValueInput = document.body.querySelector<HTMLInputElement>("input[aria-label='Attempts']")!;
    const attemptsValueField = attemptsValueInput.closest(".studio-field")!;
    expect(attemptsValueField.getAttribute("data-invalid")).toBe("true");
    expect(attemptsValueInput.getAttribute("aria-invalid")).toBe("true");
    const attemptsValueError = attemptsValueField.querySelector(".studio-field-error[role='alert']")!;
    expect(attemptsValueError.textContent).toBe("Attempts is required.");
    expect(attemptsValueInput.getAttribute("aria-describedby")).toContain(attemptsValueError.id);

    flushSync(() => root.unmount());
  });
});

function buttonByText(text: string) {
  const button = [...document.body.querySelectorAll("button")].find(candidate => candidate.textContent?.trim() === text);
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function completedRun(): ActivityDraftTestRunView {
  return {
    testRunId: "activity-test-run-1",
    draftId: "activity-draft-1",
    sourceVersionId: "version-1",
    draftRevision: 0,
    workflowExecutionId: "workflow-execution-1",
    outerActivityExecutionId: "activity-execution-1",
    status: "Completed",
    expiration: {
      sourceReferenceExpiresAt: "2026-07-20T01:00:00Z",
      receiptExpiresAt: "2026-07-27T00:00:00Z",
      sourceReferenceExpired: false,
      sourceReferenceRetained: true,
      evidenceRetained: true,
      runStillActive: false
    },
    cancellation: { capabilityAdvertised: false, available: false, status: "Unavailable" },
    requestedAt: "2026-07-20T00:00:00Z",
    updatedAt: "2026-07-20T00:00:03Z"
  } as ActivityDraftTestRunView;
}

function inspection(valueSnapshots: ActivityExecutionInspectionValueSnapshot[]) {
  return {
    activityExecutionId: "activity-execution-1",
    workflowExecutionId: "workflow-execution-1",
    startedAt: "2026-07-20T00:00:01Z",
    completedAt: "2026-07-20T00:00:01.250Z",
    outcomeNames: ["Done"],
    incidents: [],
    valueSnapshots
  } as unknown as ActivityExecutionInspection;
}

function snapshot(subject: string, name: string, value: string, capturedAt: string, inputKey?: string): ActivityExecutionInspectionValueSnapshot {
  return {
    name,
    subject,
    inputKey,
    captureMode: "Payload",
    capturedAt,
    payload: value,
    type: { kind: "alias", id: "String", schema: null },
    captureReason: "Test evidence.",
    isSensitive: false,
    metadata: {}
  } as ActivityExecutionInspectionValueSnapshot;
}

function draft(): ActivityDefinitionDraftView {
  return {
    draftId: "activity-draft-1",
    definitionId: "activity-def-1",
    tenantId: "default",
    revision: 5,
    sourceVersionId: null,
    status: "active",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: []
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: "sha256:test",
      payload: {}
    },
    layout: [],
    validation: null,
    presentationLabel: null,
    createdAt: "2026-07-20T00:00:00Z",
    updatedAt: "2026-07-20T00:00:00Z"
  };
}

async function waitFor(assertion: () => void) {
  const deadline = Date.now() + 2_000;
  while (true) {
    try {
      assertion();
      return;
    } catch (error) {
      if (Date.now() >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
}
