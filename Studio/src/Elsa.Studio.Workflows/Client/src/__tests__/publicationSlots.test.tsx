import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PublicationReviewDialog } from "../workflow-editor/WorkflowEditor";
import type { PublicationIntent, PublicationPreflight } from "../api/publishing";
import { createPublicationReview, type PublicationReviewState } from "../workflow-editor/publicationReview";
import type { WorkflowDraft } from "../workflowTypes";

let mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement } | null = null;

afterEach(() => {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("publication slot UX", () => {
  it("shows the resolved replacement policy, trigger diff, and blocks conflicts", () => {
    const onPublish = vi.fn(async () => undefined);
    const container = render(review({
      phase: "partialFailure",
      promotedVersionId: "version-2",
      failureMessage: "The promoted version was retained.",
      preflight: preflight({
      canActivate: false,
      triggers: [
        { change: "removed", key: "http:/foo", cardinality: "exclusive" },
        { change: "added", key: "http:/bar", cardinality: "exclusive" }
      ],
      conflicts: [{ key: "http:/bar", cardinality: "exclusive", publicationId: "publication-2", slotName: "canary" }]
      })
    }), onPublish);

    expect(container.textContent).toContain("host policy defaults to replacement");
    expect(container.textContent).toContain("removed http:/foo");
    expect(container.textContent).toContain("added http:/bar");
    expect(container.textContent).toContain("Conflict with slot canary");
    expect(container.textContent).toContain("promoted version was retained");
  });

  it("requires a meaningful named slot and publishes the reviewed side-by-side intent", () => {
    const onPublish = vi.fn(async () => undefined);
    const container = render(review(), onPublish);

    flushSync(() => radio(container, "Publish side by side").click());
    const input = container.querySelector<HTMLInputElement>("input[aria-label='Publication slot']")!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(input, "blue");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    flushSync(() => button(container, "Publish").click());

    expect(onPublish).toHaveBeenCalledWith({ action: "sideBySide", slotName: "blue" });
  });
});

function render(
  value: PublicationReviewState,
  onPublish: (intent: PublicationIntent) => Promise<void>
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <PublicationReviewDialog
      review={value}
      busy={false}
      onPublish={onPublish}
      onCancel={() => undefined}
    />));
  return container;
}

function review(overrides: Partial<PublicationReviewState> = {}): PublicationReviewState {
  return {
    ...createPublicationReview({
      draft: draft(),
      details: null,
      sourceVersion: null,
      policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
      slots: [],
      catalog: []
    }),
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

function preflight(overrides: Partial<PublicationPreflight> = {}): PublicationPreflight {
  return {
    definitionId: "definition-1",
    versionId: "version-2",
    slotName: "default",
    resolvedAction: "replace",
    policySource: "host",
    canActivate: true,
    triggers: [],
    conflicts: [],
    ...overrides
  };
}

function button(container: HTMLElement, text: string) {
  return [...container.querySelectorAll("button")].find(candidate => candidate.textContent === text)!;
}

function radio(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLInputElement>("input[type='radio']")]
    .find(candidate => candidate.parentElement?.textContent?.includes(label))!;
}
