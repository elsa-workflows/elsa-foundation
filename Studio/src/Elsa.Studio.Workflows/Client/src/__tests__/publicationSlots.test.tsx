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
import type { WorkflowDraft } from "../workflowTypes";
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
  it("prioritizes channel, effect, version, readiness, baseline, and compact changes", () => {
    const container = render(review());

    expect(text(container)).toContain("Publication channel");
    expect(text(container)).toContain("default (normal)");
    expect(text(container)).toContain("Replace the current publication in default");
    expect(text(container)).toContain("Assigned automatically by version policy");
    expect(text(container)).toContain("Ready to publish");
    expect(text(container)).toContain("Baseline:");
    expect(text(container)).toContain("current captured editor state is included and saved");
    expect(text(container)).not.toContain("Publication behavior");
    expect(text(container)).not.toContain("Replace authority in this slot");
  });

  it("keeps technical evidence in progressive disclosures", () => {
    const container = render(review({
      preflight: preflight({
        policyRevision: 7,
        claims: [{ key: "http:orders", cardinality: "exclusive" }]
      })
    }));

    expect(summary(container, "Changes details")).toBeDefined();
    expect(summary(container, "Advanced details")).toBeDefined();
    expect(text(container)).toContain("Policy");
    expect(text(container)).toContain("host · revision 7");
    expect(text(container)).toContain("http:orders (exclusive)");
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
    expect(text(container)).toContain("Enter a channel name.");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("derives replacement for an occupied named channel and preserves its concurrency guard", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [occupiedBlue()] }), { onReview });
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!;

    changeSelect(select, "blue");

    expect(text(container)).toContain("Replace the current publication in blue");
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

    expect(text(container)).toContain("Baseline: blue · 1.4.0");
  });

  it("falls back to the publication's version id in the baseline when no design version was fetched for the slot", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [occupiedBlue()] }), { onReview });
    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "blue");

    expect(text(container)).toContain("Baseline: blue · version-blue");
  });

  it("names the source of a channel occupied by another activation source instead of a publication", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(review({ slots: [withoutPublication(importedActivationSlot())] }), { onReview });

    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "imported");

    expect(text(container)).toContain("Replace the current activation in imported");
    expect(text(container)).toContain("imported · occupied by artifact-reconciliation (orders-bundle) · not a Studio design version");
    expect(text(container)).toContain("Not compared: imported is occupied by an activation from artifact-reconciliation (orders-bundle)");
    expect(text(container)).not.toContain("no previous publication");
    expect(onReview).toHaveBeenCalledWith(
      expect.anything(),
      { action: "replace", slotName: "imported" },
      { mode: "automatic" });
  });

  it("blocks on a foreign-owned target with an owner blocker, disabled Publish, and Not ready readiness", () => {
    const container = render(review({
      preflight: preflight({ canActivate: false, targetSlotOwner: foreignSlotOwner() })
    }));

    expect(text(container)).toContain("Publication channel is owned by another activation source");
    expect(text(container)).toContain("occupied by an activation from artifact-reconciliation (mounted-artifacts)");
    expect(text(container)).toContain("operator action");
    expect(text(container)).not.toContain("Publication channel conflicts");
    expect(text(container)).toContain("Not ready");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("keeps the trigger-conflicts blocker unchanged when there is no foreign owner", () => {
    const container = render(review({
      preflight: preflight({
        canActivate: false,
        conflicts: [{ key: "http:orders", cardinality: "exclusive", publicationId: "publication-9", slotName: "default" }]
      })
    }));

    expect(text(container)).toContain("Publication channel conflicts");
    expect(text(container)).toContain("Conflict with default: http:orders");
    expect(text(container)).not.toContain("owned by another activation source");
    expect(text(container)).toContain("Not ready");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("shows the cause-neutral message when an older host blocks with empty conflicts and no owner", () => {
    const blockedPreflight = preflight({ canActivate: false, targetSlotOwner: undefined });
    const container = render(review({ preflight: blockedPreflight }));

    expect(text(container)).toContain(publicationBlockedMessage(blockedPreflight));
    expect(text(container)).not.toContain("owned by another activation source");
    expect(text(container)).not.toContain("Publication channel conflicts");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("shows both blockers when the target is foreign-owned and has trigger conflicts", () => {
    const container = render(review({
      preflight: preflight({
        canActivate: false,
        targetSlotOwner: foreignSlotOwner(),
        conflicts: [{ key: "http:orders", cardinality: "exclusive", publicationId: "publication-9", slotName: "default" }]
      })
    }));

    expect(text(container)).toContain("Publication channel is owned by another activation source");
    expect(text(container)).toContain("Publication channel conflicts");
    expect(text(container)).toContain("Conflict with default: http:orders");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("states that the current publication is unknown when the backend cannot provide publication slots", () => {
    const visible = render(review());
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
    expect(summary(unsupported, "Edit version")).toBeUndefined();
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
    expect(summary(supported, "Edit version")).toBeDefined();
    expect(text(supported)).toContain("2.0.0 · assigned automatically by version policy");
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

    flushSync(() => summary(container, "Edit version")!.click());
    const exactRadio = [...container.querySelectorAll<HTMLInputElement>("input[type='radio']")]
      .find(input => input.parentElement?.textContent?.includes("Exact semantic version"))!;
    flushSync(() => exactRadio.click());
    const input = container.querySelector<HTMLInputElement>("input[aria-label='Exact semantic version']")!;
    setInput(input, "2.1.0-rc.1");

    expect(onReview).toHaveBeenLastCalledWith(
      expect.anything(),
      { action: "replace", slotName: "default" },
      { mode: "exact", requestedVersion: "2.1.0-rc.1" });
    expect(text(container)).toContain("2.1.0-rc.1");
    expect(text(container)).not.toContain("2.0.0 · assigned automatically");
    expect(button(container, "Publish").disabled).toBe(true);
    expect(text(container)).toContain("Not ready");
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

    expect(text(container)).toContain("Workflow is published");
    expect(text(container)).toContain("Version 2.1.0 is active");
    expect(button(container, "Close").classList.contains("wf-primary-action")).toBe(true);
    expect(button(container, "Open published executable").classList.contains("wf-primary-action")).toBe(false);
    expect(document.activeElement).toBe(container.querySelector("#publication-success-title"));
    flushSync(() => button(container, "Open published executable").click());
    expect(onOpen).toHaveBeenCalledOnce();
  });

  it("renders retained-promotion recovery without the review form", () => {
    const container = render(review({
      phase: "partialFailure",
      proposedVersion: "2.1.0",
      promotedVersionId: "version-2",
      failureMessage: "Activation timed out. The promoted version was retained."
    }));

    expect(text(container)).toContain("version was retained, but the channel was not activated");
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
    slots: [],
    catalog: []
  });
  return {
    ...value,
    preflight: preflight(),
    ...overrides
  };
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
