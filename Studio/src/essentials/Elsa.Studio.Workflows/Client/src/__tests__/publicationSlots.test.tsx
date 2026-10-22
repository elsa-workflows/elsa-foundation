import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PublicationReviewDialog } from "../workflow-editor/WorkflowEditor";
import { activationSlotReadsUnavailableReason, type PublicationIntent } from "../api/publishing";
import {
  createPublicationReview,
  publicationBlockedMessage,
  type PublicationReviewState,
  type PublicationVersionSelection
} from "../workflow-editor/publicationReview";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import {
  foreignSlotOwner,
  importedActivationSlot,
  publicationPreflight,
  publishedSlot,
  versionDetails,
  withoutPublication
} from "./fixtures/publicationSlots";

let mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement } | null = null;

afterEach(() => {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("publication channel UX", () => {
  it("starts with the proposed version and compact change summary, with all three disclosures collapsed", () => {
    const container = render(review({
      proposedVersion: "2.0.0",
      preflight: preflight({
        triggers: [{ change: "added", key: "http:orders", cardinality: "exclusive" }]
      })
    }));
    expect(container.querySelector("h3")?.textContent).toBe("Publish workflow");
    expect(container.querySelector(".wf-publication-header p")?.textContent).toBe("Update the published workflow with your latest changes.");
    expect(container.querySelectorAll(".wf-publication-body > details")).toHaveLength(3);
    const options = summary(container, "Publishing options")!.parentElement as HTMLDetailsElement;
    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const technical = summary(container, "Technical details")!.parentElement as HTMLDetailsElement;
    expect(options.open).toBe(false);
    expect(changes.open).toBe(false);
    expect(technical.open).toBe(false);
    expect(container.querySelector("select[aria-label='Publication channel']")?.closest("details")).toBe(options);
    expect(container.querySelector(".wf-publication-baseline")?.closest("details")).toBe(changes);
    expect(text(container)).toContain("Version 2.0.0");
    expect(container.querySelector(".wf-publication-change-summary")?.textContent).toBe("Changes: 1 activity · 1 trigger");
    expect(container.querySelector("#publication-review-status")?.getAttribute("data-visible")).toBe("false");
    expect(button(container, "Publish").disabled).toBe(false);
    expect(text(container)).not.toContain("captured editor state");
  });

  it("uses occupancy when an authoritative preflight is not available", () => {
    const container = render(review({ slots: [], preflight: undefined }));

    expect(container.querySelector(".wf-publication-header p")?.textContent).toBe("Make your latest changes available to run.");
  });

  it("keeps technical evidence in its own collapsed top-level disclosure", () => {
    const container = render(review({
      preflight: preflight({
        policyRevision: 7,
        claims: [{ key: "http:orders", cardinality: "exclusive" }]
      })
    }));

    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const technical = summary(container, "Technical details")!.parentElement as HTMLDetailsElement;
    expect(technical.open).toBe(false);
    expect(technical.parentElement?.closest("details")).toBeNull();
    expect(technical.textContent).toContain("Policy");
    expect(technical.textContent).toContain("host · revision 7");
    expect(technical.textContent).toContain("http:orders (exclusive)");
    expect(changes.textContent).not.toContain("http:orders");
  });

  it("shows grouped conflict counts while exact trigger and publication identifiers stay in Technical details", () => {
    const triggerKey = "sha256:8a80851546a1f1875cf5c1d78dbbc51ac6e42d8f8f47b3f77b6e5db34f01c3cb";
    const publicationId = "publication-7f4d2a1c-9b6e-4f20-a45d-01a28dbe3381";
    const container = render(review({
      proposedVersion: "2.0.0",
      preflight: preflight({
        canActivate: false,
        claims: [{ key: triggerKey, cardinality: "exclusive" }],
        triggers: [{ change: "added", key: triggerKey, cardinality: "exclusive" }],
        conflicts: [
          { key: triggerKey, cardinality: "exclusive", publicationId, slotName: "default" },
          { key: triggerKey, cardinality: "exclusive", publicationId: `${publicationId}-other`, slotName: "default" }
        ]
      })
    }));

    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const technical = summary(container, "Technical details")!.parentElement as HTMLDetailsElement;
    const conflictAlert = container.querySelector<HTMLElement>(".wf-publication-risks[role='alert']")!;
    expect(conflictAlert.textContent).toContain("1 trigger conflicts with channel default.");
    expect(conflictAlert.textContent).not.toContain(triggerKey);
    expect(conflictAlert.textContent).not.toContain(publicationId);
    expect(container.querySelector(".wf-publication-change-summary")?.textContent).toBe("Changes: 1 activity · 1 trigger");
    expect(changes.textContent).toContain("1 added");
    expect(changes.textContent).not.toContain(triggerKey);
    expect(changes.textContent).not.toContain(publicationId);
    expect(technical.open).toBe(false);
    expect(technical.textContent).toContain(triggerKey);
    expect(technical.textContent).toContain(publicationId);
  });

  it.each([
    ["missing", undefined],
    ["stale", publicationPreflight({ slotName: "blue" })]
  ] as const)("does not present local trigger differences as authoritative when preflight is %s", (_state, preflightValue) => {
    const container = render(reviewWithLocalTrigger({ preflight: preflightValue }));
    const summaryLines = [...container.querySelectorAll<HTMLElement>(".wf-publication-change-summary")]
      .map(line => line.textContent);
    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const triggerRow = [...changes.querySelectorAll("dt")].find(term => term.textContent === "Triggers");

    expect(summaryLines).toEqual(["Changes: 1 activity", "Trigger changes have not been checked."]);
    expect(triggerRow?.nextElementSibling?.textContent).toBe("Not checked");
  });

  it("shows matching-preflight trigger counts when the target has no comparable design baseline", () => {
    const container = render(reviewWithLocalTrigger({
      policy: { defaultAction: "replace", defaultSlotName: "imported", source: "host" },
      slots: [withoutPublication(importedActivationSlot())],
      intent: { action: "replace", slotName: "imported" },
      preflight: publicationPreflight({
        slotName: "imported",
        triggers: [{ change: "added", key: "http:orders", cardinality: "exclusive" }]
      })
    }));
    const summaryLines = [...container.querySelectorAll<HTMLElement>(".wf-publication-change-summary")]
      .map(line => line.textContent);

    expect(summaryLines).toContain("Trigger changes: 1 added");
    expect(summaryLines[0]).toContain("Not compared: imported is occupied by an activation from artifact-reconciliation (orders-bundle)");
  });

  it("does not count retained-only preflight triggers as changes", () => {
    const container = render(reviewWithLocalTrigger({
      preflight: publicationPreflight({
        triggers: [{ change: "retained", key: "http:orders", cardinality: "exclusive" }]
      })
    }));
    const compactSummary = container.querySelector<HTMLElement>(".wf-publication-change-summary")!;
    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const triggerRow = [...changes.querySelectorAll("dt")].find(term => term.textContent === "Triggers");

    expect(compactSummary.textContent).toBe("Changes: 1 activity");
    expect(triggerRow?.nextElementSibling?.textContent).toBe("1 retained");
  });

  it("lists existing channels and exposes a distinct create-new path", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [occupiedBlue()] }), { onReview });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!;

    expect([...select.options].map(option => option.textContent)).toEqual([
      "default (normal)",
      "blue",
      "Create new channel…"
    ]);

    changeSelect(select, "__create-publication-channel__");

    expect(container.querySelector("input[aria-label='New publication channel']")).not.toBeNull();
    expect(text(container)).toContain("Enter a channel name before publishing.");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("derives replacement for an occupied named channel and preserves its concurrency guard", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [occupiedBlue()] }), { onReview });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!;

    changeSelect(select, "blue");

    expect(container.querySelector(".wf-publication-header p")?.textContent).toBe("Update the published workflow in blue.");
    expect(button(container, "Publish").disabled).toBe(true);
    expect(onReview).toHaveBeenCalledWith(
      expect.anything(),
      { action: "replace", slotName: "blue", expectedPublicationId: "publication-blue" },
      { mode: "automatic" });
  });

  it("shows the fetched design version's label in the baseline when one was fetched for the slot", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({
      slots: [occupiedBlue()],
      slotVersions: { blue: versionDetails("version-blue", { version: "1.4.0", state: { rootActivity: null } }) }
    }), { onReview });
    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "blue");

    expect(text(container)).toContain("Compared with blue · 1.4.0");
  });

  it("falls back to the publication's version id in the baseline when no design version was fetched for the slot", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [occupiedBlue()] }), { onReview });
    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "blue");

    expect(text(container)).toContain("Compared with blue · version-blue");
  });

  it("names the source of a channel occupied by another activation source instead of a publication", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [withoutPublication(importedActivationSlot())] }), { onReview });

    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "imported");

    expect(text(container)).toContain("Publish your changes to imported.");
    expect(text(container)).toContain("imported · occupied by artifact-reconciliation (orders-bundle) · not a Studio design version");
    expect(text(container)).toContain("Not compared: imported is occupied by an activation from artifact-reconciliation (orders-bundle)");
    expect(text(container)).not.toContain("no previous publication");
    expect(onReview).toHaveBeenCalledWith(
      expect.anything(),
      { action: "replace", slotName: "imported" },
      { mode: "automatic" });
  });

  it("blocks a foreign-owned target and asks authors to review the blocker", () => {
    const container = render(review({
      preflight: preflight({ canActivate: false, targetSlotOwner: foreignSlotOwner() })
    }));

    expect(text(container)).toContain("This channel is managed elsewhere");
    expect(text(container)).toContain("managed by artifact-reconciliation (mounted-artifacts)");
    expect(text(container)).toContain("Choose another channel");
    expect(text(container)).not.toContain("These triggers are already in use");
    expect(text(container)).toContain("Review the highlighted issue before publishing.");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("keeps the trigger-conflicts blocker unchanged when there is no foreign owner", () => {
    const container = render(review({
      versionPreflightSupported: true,
      preflight: preflight({
        canActivate: false,
        conflicts: [{ key: "http:orders", cardinality: "exclusive", publicationId: "publication-9", slotName: "default" }]
      })
    }), { onReview: vi.fn(async () => undefined) });

    expect(text(container)).toContain("These triggers are already in use");
    expect(text(container)).toContain("1 trigger conflicts with channel default.");
    const technical = summary(container, "Technical details")!.parentElement as HTMLDetailsElement;
    expect(technical.open).toBe(false);
    expect(technical.textContent).toContain("http:orders");
    expect(technical.textContent).toContain("publication-9");
    expect(text(container)).not.toContain("owned by another activation source");
    expect(text(container)).toContain("Review the highlighted issue before publishing.");
    expect(container.querySelector("#publication-review-status")?.textContent).toBe("Review the highlighted issue before publishing.");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("shows the cause-neutral message when an older host blocks with empty conflicts and no owner", () => {
    const blockedPreflight = preflight({ canActivate: false, targetSlotOwner: undefined });
    const container = render(review({ preflight: blockedPreflight }));

    expect(text(container)).toContain(publicationBlockedMessage(blockedPreflight));
    expect(text(container)).not.toContain("owned by another activation source");
    expect(text(container)).not.toContain("These triggers are already in use");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("shows validation blockers without opening changes or options", () => {
    const container = render(review({
      phase: "validationBlocked",
      validationErrors: ["An HTTP endpoint needs a path."]
    }));
    const alert = container.querySelector("[role='alert']")!;
    expect(alert.textContent).toContain("An HTTP endpoint needs a path.");
    expect(alert.closest("details")).toBeNull();
    expect(container.querySelector("details[open]")).toBeNull();
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("does not announce a version check while draft validation blocks review", () => {
    const container = render(review({
      phase: "validationBlocked",
      versionPreflightSupported: true,
      validationErrors: ["An HTTP endpoint needs a path."]
    }));

    expect(text(container)).not.toContain("Checking your version choice");
    expect(container.querySelector("[role='alert']")?.textContent).toContain("An HTTP endpoint needs a path.");
    expect(container.querySelector("#publication-review-status")?.textContent).toBe("Fix the highlighted issues before publishing.");
  });

  it("says it is checking the workflow while selected-version evidence is missing", () => {
    const container = render(review({ versionPreflightSupported: true }), {
      onReview: vi.fn(async () => undefined)
    });

    expect(container.querySelector("#publication-review-status")?.textContent).toBe("Checking workflow…");
    expect(text(container)).not.toContain("Review the highlighted issue before publishing.");
  });

  it("does not announce a version check for an exact version with an empty new channel", () => {
    const container = render(review({
      versionPreflightSupported: true,
      versionSelection: { mode: "exact", requestedVersion: "2.3.0" }
    }));

    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "__create-publication-channel__");

    expect(text(container)).toContain("Enter a channel name before publishing.");
    expect(text(container)).not.toContain("Checking your version choice");
    expect(container.querySelector("#publication-review-status")?.textContent).not.toBe("Checking workflow…");
  });

  it("uses the preflight replacement action when slot occupancy is unavailable", () => {
    const container = render(review({
      intent: { action: "replace", slotName: "blue" },
      preflight: preflight({ slotName: "blue", resolvedAction: "replace" }),
      slotsUnavailableReason: "The host could not list activation slots."
    }));

    expect(container.querySelector(".wf-publication-header p")?.textContent).toBe("Update the published workflow in blue.");
  });

  it("shows both blockers when the target is foreign-owned and has trigger conflicts", () => {
    const container = render(review({
      preflight: preflight({
        canActivate: false,
        targetSlotOwner: foreignSlotOwner(),
        conflicts: [{ key: "http:orders", cardinality: "exclusive", publicationId: "publication-9", slotName: "default" }]
      })
    }));

    expect(text(container)).toContain("This channel is managed elsewhere");
    expect(text(container)).toContain("These triggers are already in use");
    expect(text(container)).toContain("1 trigger conflicts with channel default.");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("states that the current publication is unknown when the backend cannot provide publication slots", () => {
    const visible = render(review({ slots: [] }));
    expect(text(visible)).toContain("New channel · default · no previous publication");
    expect(visible.querySelector("[role='note']")).toBeNull();
    unmount();

    const container = render(review({ slotsUnavailableReason: activationSlotReadsUnavailableReason }));

    expect(container.querySelector("[role='note']")?.textContent).toBe(activationSlotReadsUnavailableReason);
    expect(text(container)).toContain("default · current publication unknown");
    expect(text(container)).toContain("Not compared: the current publication in this channel is unknown on this backend.");
    expect(text(container)).not.toContain("no previous publication");
    expect(text(container)).toContain("Ready to publish");
  });

  it("shows exact-version editing only when the backend advertises it", () => {
    const unsupported = render(review());
    expect(unsupported.querySelector("fieldset")).toBeNull();
    unmount();

    const supported = render(review({
      exactVersionSupported: true,
      versionPreflightSupported: true,
      versionPreflight: {
        isReady: true,
        assignmentMode: "automatic",
        resolvedVersion: "2.0.0",
        latestVersion: "1.0.0",
        issues: []
      }
    }));
    expect(supported.querySelector("fieldset")).not.toBeNull();
    expect(text(supported)).toContain("Next version: 2.0.0");
  });

  it("automatically requests authoritative review when the exact version changes", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({
      exactVersionSupported: true,
      versionPreflightSupported: true,
      versionPreflight: {
        isReady: true,
        assignmentMode: "automatic",
        resolvedVersion: "2.0.0",
        latestVersion: "1.0.0",
        issues: []
      }
    }), { onReview });

    flushSync(() => summary(container, "Publishing options")!.click());
    const exactRadio = [...container.querySelectorAll<HTMLInputElement>("input[type='radio']")]
      .find(input => input.parentElement?.textContent?.includes("Exact version"))!;
    flushSync(() => exactRadio.click());
    const input = container.querySelector<HTMLInputElement>("input[aria-label='Exact semantic version']")!;
    setInput(input, "2.1.0-rc.1");

    expect(onReview).toHaveBeenLastCalledWith(
      expect.anything(),
      { action: "replace", slotName: "default", expectedPublicationId: "publication-default" },
      { mode: "exact", requestedVersion: "2.1.0-rc.1" });
    expect(text(container)).toContain("2.1.0-rc.1");
    const selectedVersion = container.querySelector(".wf-publication-body > .wf-publication-selection")!;
    expect(selectedVersion.textContent).toContain("2.1.0-rc.1");
    expect(selectedVersion.closest("details")).toBeNull();
    expect(text(container)).not.toContain("Next version: 2.0.0");
    expect(button(container, "Publish").disabled).toBe(true);
    expect(container.querySelector("#publication-review-status")?.textContent).toBe("Checking workflow…");
  });

  it("keeps a failed authoritative review stable until the author retries or changes the selection", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({
      preflight: undefined,
      reviewFailed: true,
      failureMessage: "Authoritative review failed. No changes were saved, promoted, or published."
    }), { onReview });

    expect(onReview).not.toHaveBeenCalled();
    flushSync(() => button(container, "Retry review").click());
    expect(onReview).toHaveBeenCalledOnce();
  });

  it("does not submit while the current review is blocked", () => {
    const onPublish = vi.fn(async () => undefined);
    const container = render(review({
      preflight: undefined,
      reviewPending: true
    }), { onPublish });

    flushSync(() => container.querySelector("form")!.dispatchEvent(
      new Event("submit", { bubbles: true, cancelable: true })));

    expect(onPublish).not.toHaveBeenCalled();
  });

  it("renders success as a compact outcome with both visible footer actions", () => {
    const onOpen = vi.fn();
    const container = render(review({
      phase: "success",
      proposedVersion: "2.1.0",
      published: {
        publicationId: "publication-2",
        definitionId: "definition-1",
        versionId: "version-2",
        artifactId: "artifact-2",
        slotName: "default",
        sourceReferenceId: "reference-2",
        status: "active"
      }
    }), { onOpenPublishedExecutable: onOpen });

    expect(text(container)).toContain("Published");
    expect(text(container)).toContain("Version 2.1.0 is active");
    expect(button(container, "Close").classList.contains("wf-primary-action")).toBe(true);
    expect(button(container, "View published workflow").classList.contains("wf-primary-action")).toBe(false);
    expect(document.activeElement).toBe(container.querySelector("#publication-success-title"));
    flushSync(() => button(container, "View published workflow").click());
    expect(onOpen).toHaveBeenCalledOnce();
  });

  it("renders retained-promotion recovery without the review form", () => {
    const container = render(review({
      phase: "partialFailure",
      proposedVersion: "2.1.0",
      promotedVersionId: "version-2",
      failureMessage: "Activation timed out. The promoted version was retained."
    }));

    expect(text(container)).toContain("Version 2.1.0 was retained. Channel default was not activated.");
    expect(button(container, "Retry publication")).toBeDefined();
    expect(button(container, "Close")).toBeDefined();
    expect(container.querySelector("select[aria-label='Publication channel']")).toBeNull();
    expect(document.activeElement).toBe(container.querySelector("#publication-recovery-title"));
  });

  it("uses a fixed three-region shell so body alerts cannot displace the footer", () => {
    const container = render(review({
      phase: "savedFailure",
      validationErrors: ["A".repeat(400)],
      failureMessage: "The draft was saved. No version or publication was created."
    }));
    const form = container.querySelector("form")!;

    expect(form.children[0].classList.contains("wf-publication-header")).toBe(true);
    expect(form.children[1].classList.contains("wf-publication-body")).toBe(true);
    expect(form.children[2].classList.contains("wf-publication-footer")).toBe(true);
    expect(button(container, "Close")).toBeDefined();
  });
});

function render(
  value: PublicationReviewState,
  options: {
    busy?: boolean;
    onReview?: (
      review: PublicationReviewState,
      intent: PublicationIntent,
      version: PublicationVersionSelection
    ) => Promise<void>;
    onPublish?: (intent: PublicationIntent, version?: PublicationVersionSelection) => Promise<void>;
    onOpenPublishedExecutable?: () => void;
  } = {}
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <PublicationReviewDialog
      review={value}
      busy={options.busy ?? false}
      onReview={options.onReview}
      onPublish={options.onPublish ?? vi.fn(async () => undefined)}
      onCancel={() => undefined}
      onOpenPublishedExecutable={options.onOpenPublishedExecutable}
    />));
  return container;
}

function unmount() {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
}

function review(overrides: Partial<PublicationReviewState> = {}): PublicationReviewState {
  const value = createPublicationReview({
    draft: draft(),
    details: null,
    slotVersions: {},
    policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
    slots: [publishedSlot("default")],
    catalog: []
  });
  const state = {
    ...value,
    preflight: preflight(),
    ...overrides
  };
  const publicationId = state.intent.action === "replace"
    ? state.slots.find(slot => slot.slotName === state.intent.slotName)?.publication?.publicationId
    : undefined;
  if (publicationId) state.intent = { ...state.intent, expectedPublicationId: publicationId };
  return state;
}

function reviewWithLocalTrigger(overrides: Partial<PublicationReviewState> = {}): PublicationReviewState {
  const state = review(overrides);
  state.catalog = [localTrigger];
  state.triggerActivityVersionIds = [localTrigger.activityVersionId];
  state.draftSnapshot.state.rootActivity = {
    nodeId: "trigger-root",
    activityVersionId: localTrigger.activityVersionId,
    inputs: [],
    outputs: []
  };
  return state;
}

function draft(): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
    layout: [],
    validationErrors: []
  };
}

const preflight = publicationPreflight;

function occupiedBlue() {
  return publishedSlot("blue");
}

function button(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(candidate => candidate.textContent === label)!;
}

function summary(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLElement>("summary")]
    .find(candidate => candidate.textContent === label);
}

function changeSelect(select: HTMLSelectElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!;
    setter.call(select, value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function setInput(input: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function text(container: HTMLElement) {
  return container.textContent ?? "";
}

const localTrigger = {
  activityVersionId: "test-http-trigger-v1",
  activityTypeKey: "Test.HttpTrigger",
  version: "1.0.0",
  category: "Triggers",
  displayName: "HTTP Trigger",
  executionType: "Trigger",
  inputs: [],
  outputs: []
} satisfies ActivityCatalogItem;
