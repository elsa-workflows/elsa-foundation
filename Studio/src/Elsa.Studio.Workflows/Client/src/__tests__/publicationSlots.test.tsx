import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PublicationReviewDialog } from "../workflow-editor/WorkflowEditor";
import type { PublicationPreflight } from "../api/publishing";

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
    const container = render(preflight({
      canActivate: false,
      triggers: [
        { change: "removed", key: "http:/foo", cardinality: "exclusive" },
        { change: "added", key: "http:/bar", cardinality: "exclusive" }
      ],
      conflicts: [{ key: "http:/bar", cardinality: "exclusive", publicationId: "publication-2", slotName: "canary" }]
    }), {}, onPublish);

    expect(container.textContent).toContain("host policy resolved to replacement");
    expect(container.textContent).toContain("removed: http:/foo");
    expect(container.textContent).toContain("added: http:/bar");
    expect(container.textContent).toContain("Conflict with slot canary");
    expect(button(container, "Publish").disabled).toBe(true);
  });

  it("requires a meaningful named slot and re-preflights side-by-side publication", () => {
    const onReview = vi.fn(async () => undefined);
    const container = render(preflight(), {}, vi.fn(async () => undefined), onReview);

    flushSync(() => radio(container, "Publish side by side").click());
    const input = container.querySelector<HTMLInputElement>("input[aria-label='Publication slot']")!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(input, "blue");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    flushSync(() => button(container, "Review changes").click());

    expect(onReview).toHaveBeenCalledWith({ action: "sideBySide", slotName: "blue" });
  });
});

function render(
  value: PublicationPreflight,
  reviewedIntent: {},
  onPublish: () => Promise<void>,
  onReview: (intent: any) => Promise<void> = async () => undefined
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <PublicationReviewDialog
      preflight={value}
      reviewedIntent={reviewedIntent}
      busy={false}
      onReview={onReview}
      onPublish={onPublish}
      onCancel={() => undefined}
    />));
  return container;
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
