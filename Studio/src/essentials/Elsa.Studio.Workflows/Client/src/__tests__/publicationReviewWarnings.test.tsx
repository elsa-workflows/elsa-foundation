import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PublicationReviewDialog } from "../workflow-editor/WorkflowEditor";
import {
  createPublicationReview,
  isWarningSeverity,
  splitPublicationValidations,
  withPublicationWarnings,
  type PublicationReviewState
} from "../workflow-editor/publicationReview";
import type { ValidationError, WorkflowDraft } from "../workflowTypes";
import { publicationPreflight, publishedSlot } from "./fixtures/publicationSlots";

// Spec 261 FR-515: the publication review reads each validation's severity. Warnings (for example the compensation
// replay-safe warning, or a Connections warning) are listed under their own heading with an icon and never block; errors
// still block; an entry without a severity is an error.

const replaySafeWarning = "\"Charge card\" is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none.";
const connectionWarning = "The connection 'crm' is not available in this environment.";

let mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement } | null = null;

afterEach(() => {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("publication review warnings", () => {
  it("splits errors from warnings and reads a missing severity as an error", () => {
    expect(splitPublicationValidations([
      { message: "A start activity is required." },
      { message: replaySafeWarning, severity: "Warning" },
      { message: "Bad input.", severity: "Error" },
      { message: connectionWarning, severity: "warning" },
      { message: "  ", severity: "Warning" },
      { message: null }
    ])).toEqual({
      errors: ["A start activity is required.", "Bad input."],
      warnings: [replaySafeWarning, connectionWarning]
    });
    expect(isWarningSeverity(undefined)).toBe(false);
    expect(isWarningSeverity(null)).toBe(false);
    expect(isWarningSeverity("Error")).toBe(false);
    expect(isWarningSeverity(" Warning ")).toBe(true);
  });

  it("opens a publishable review when the draft has warnings only", () => {
    const value = review([{ path: "scope/steps/0", type: "CompensationScope/ReplaySafeStep", message: replaySafeWarning, severity: "Warning" }]);

    expect(value).toMatchObject({ phase: "review", executableStatus: "ready", validationErrors: [], validationWarnings: [replaySafeWarning] });
  });

  it("blocks a review that has an error and a warning, and still lists the warning", () => {
    const value = review([
      { message: "Step 2 needs a name.", severity: "Error" },
      { message: replaySafeWarning, severity: "Warning" }
    ]);

    expect(value).toMatchObject({ phase: "validationBlocked", executableStatus: "blocked", validationErrors: ["Step 2 needs a name."], validationWarnings: [replaySafeWarning] });
  });

  it("treats an entry without a severity as a blocking error", () => {
    expect(review([{ message: "Workflow input 'x' is missing." }])).toMatchObject({ phase: "validationBlocked", validationErrors: ["Workflow input 'x' is missing."], validationWarnings: [] });
  });

  it("does not block on a Connections warning", () => {
    const value = review([{ path: "crm-call/inputs/connection", type: "Connections/Unavailable", message: connectionWarning, severity: "Warning" }]);

    expect(value.validationErrors).toEqual([]);
    expect(value.validationWarnings).toEqual([connectionWarning]);
  });

  it("adds the server's warnings once each and ignores its errors", () => {
    const value = withPublicationWarnings(review([{ message: replaySafeWarning, severity: "Warning" }]), [
      { message: replaySafeWarning, severity: "Warning" },
      { message: connectionWarning, severity: "Warning" },
      { message: connectionWarning, severity: "Warning" },
      { message: "Server error.", severity: "Error" }
    ]);

    expect(value.validationWarnings).toEqual([replaySafeWarning, connectionWarning]);
    expect(value.validationErrors).toEqual([]);
    expect(withPublicationWarnings(value, null)).toBe(value);
  });

  it("lists warnings with an icon under their own heading and keeps Publish enabled", () => {
    const container = render(ready(review([{ message: replaySafeWarning, severity: "Warning" }])));

    const warnings = container.querySelector<HTMLElement>(".wf-publication-warnings")!;
    expect(warnings).not.toBeNull();
    expect(warnings.querySelector("h4")?.textContent).toContain("Warnings");
    expect(warnings.querySelector(".wf-publication-warnings-mark")?.getAttribute("aria-hidden")).toBe("true");
    expect(warnings.textContent).toContain("These do not block publishing.");
    expect([...warnings.querySelectorAll("li")].map(item => item.textContent)).toEqual([replaySafeWarning]);
    expect(container.querySelector(".wf-publication-risks[role='alert']")).toBeNull();
    expect(button(container, "Publish").disabled).toBe(false);
  });

  it("keeps Publish disabled while an error is listed beside a warning", () => {
    const container = render(ready(review([
      { message: "Step 2 needs a name.", severity: "Error" },
      { message: replaySafeWarning, severity: "Warning" }
    ])));

    expect(container.querySelector(".wf-publication-risks")?.textContent).toContain("Step 2 needs a name.");
    expect(container.querySelector(".wf-publication-warnings")?.textContent).toContain("Charge card");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("shows no warnings section when there are none", () => {
    const container = render(ready(review([])));

    expect(container.querySelector(".wf-publication-warnings")).toBeNull();
    expect(button(container, "Publish").disabled).toBe(false);
  });
});

function review(validationErrors: ValidationError[]): PublicationReviewState {
  return createPublicationReview({
    draft: draft(validationErrors),
    details: null,
    slotVersions: {},
    policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
    slots: [publishedSlot("default")],
    catalog: []
  });
}

// The review once the authoritative preflight for its replace intent came back ready.
function ready(value: PublicationReviewState): PublicationReviewState {
  const publicationId = value.slots.find(slot => slot.slotName === value.intent.slotName)?.publication?.publicationId;
  return { ...value, preflight: publicationPreflight(), intent: { ...value.intent, expectedPublicationId: publicationId } };
}

function draft(validationErrors: ValidationError[]): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
    layout: [],
    validationErrors
  };
}

function render(value: PublicationReviewState) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <PublicationReviewDialog
      review={value}
      busy={false}
      onPublish={vi.fn(async () => undefined)}
      onCancel={() => undefined}
    />));
  return container;
}

function button(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === label)!;
}
