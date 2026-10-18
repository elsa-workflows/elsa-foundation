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
  it("starts with a short confirmation and keeps changes and publishing options collapsed", () => {
    const container = render(review());
    expect(container.querySelector("h3")?.textContent).toBe("Publish workflow");
    expect(container.querySelector(".wf-publication-header p")?.textContent).toBe("Update the published workflow with your latest changes.");
    expect(container.querySelectorAll(".wf-publication-body > details")).toHaveLength(2);
    const options = summary(container, "Publishing options")!.parentElement as HTMLDetailsElement;
    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    expect(options.open).toBe(false);
    expect(changes.open).toBe(false);
    expect(container.querySelector("select[aria-label='Publication channel']")?.closest("details")).toBe(options);
    expect(container.querySelector(".wf-publication-baseline")?.closest("details")).toBe(changes);
    expect(container.querySelector("#publication-review-status")?.getAttribute("data-visible")).toBe("false");
    expect(button(container, "Publish").disabled).toBe(false);
    expect(text(container)).not.toContain("captured editor state");
  });

  it("keeps technical evidence in progressive disclosures", () => {
    const container = render(review({
      preflight: preflight({
        policyRevision: 7,
        claims: [{ key: "http:orders", cardinality: "exclusive" }]
      })
    }));

    const changes = summary(container, "Review changes")!.parentElement as HTMLDetailsElement;
    const technical = summary(container, "Technical details")!.parentElement as HTMLDetailsElement;
    expect(technical.open).toBe(false);
    expect(technical.parentElement?.closest("details")).toBe(changes);
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
      preflight: preflight({
        canActivate: false,
        conflicts: [{ key: "http:orders", cardinality: "exclusive", publicationId: "publication-9", slotName: "default" }]
      })
    }));

    expect(text(container)).toContain("These triggers are already in use");
    expect(text(container)).toContain("Conflict with default: http:orders");
    expect(text(container)).not.toContain("owned by another activation source");
    expect(text(container)).toContain("Review the highlighted issue before publishing.");
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
  });

  it("does not announce a version check for an exact version with an empty new channel", () => {
    const container = render(review({
      versionPreflightSupported: true,
      versionSelection: { mode: "exact", requestedVersion: "2.3.0" }
    }));

    changeSelect(container.querySelector<HTMLSelectElement>("select[aria-label='Publication channel']")!, "__create-publication-channel__");

    expect(text(container)).toContain("Enter a channel name before publishing.");
    expect(text(container)).not.toContain("Checking your version choice");
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
      { action: "replace", slotName: "default" },
      { mode: "exact", requestedVersion: "2.1.0-rc.1" });
    expect(text(container)).toContain("2.1.0-rc.1");
    const selectedVersion = container.querySelector(".wf-publication-body > .wf-publication-selection")!;
    expect(selectedVersion.textContent).toContain("2.1.0-rc.1");
    expect(selectedVersion.closest("details")).toBeNull();
    expect(text(container)).not.toContain("Next version: 2.0.0");
    expect(button(container, "Publish").disabled).toBe(true);
    expect(text(container)).toContain("Review the highlighted issue before publishing.");
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
