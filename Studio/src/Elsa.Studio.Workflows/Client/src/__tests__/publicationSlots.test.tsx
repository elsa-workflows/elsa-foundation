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
  it("shows the authoritative resolved target, policy, claims, and executable impact before confirmation", () => {
    const container = render(review({
      preflight: preflight({
        policyRevision: 7,
        claims: [{ key: "http:orders", cardinality: "exclusive" }]
      })
    }), vi.fn(async () => undefined));

    expect(container.textContent).toContain("Resolved action");
    expect(container.textContent).toContain("replace");
    expect(container.textContent).toContain("Target slot");
    expect(container.textContent).toContain("default");
    expect(container.textContent).toContain("Policy source");
    expect(container.textContent).toContain("host (revision 7)");
    expect(container.textContent).toContain("http:orders (exclusive)");
    expect(container.textContent).toContain("Create a new executable source reference in slot default");
    expect(container.textContent).toContain("Server preflight completed — validation passed.");
    expect(container.textContent).not.toContain("Ready for server preflight");
  });

  it("reports an authoritative preflight in progress distinctly", () => {
    const container = render(review({ preflight: undefined }), vi.fn(async () => undefined), true);

    expect(container.textContent).toContain("Server preflight in progress.");
  });

  it("reports a failed authoritative preflight distinctly", () => {
    const container = render(review({
      preflight: undefined,
      failureMessage: "Server preflight failed. Review the target again. Connection unavailable."
    }), vi.fn(async () => undefined));

    expect(container.textContent).toContain("Server preflight failed — review the target again.");
  });

  it("reports a stale authoritative preflight distinctly", () => {
    const container = render(review({
      phase: "partialFailure",
      preflight: undefined,
      promotedVersionId: "version-2",
      failureMessage: "The reviewed publication target became stale before activation."
    }), vi.fn(async () => undefined));

    expect(container.textContent).toContain("Server preflight is stale — review the target again.");
  });

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

  it("requires a meaningful named slot and requests authoritative review for a changed target", () => {
    const onPublish = vi.fn(async () => undefined);
    const container = render(review(), onPublish);

    flushSync(() => radio(container, "Publish side by side").click());
    setInput(container, "blue");
    flushSync(() => button(container, "Review target").click());

    expect(onPublish).toHaveBeenCalledWith({ action: "sideBySide", slotName: "blue" });
  });

  it("treats an occupied side-by-side slot as a protected replacement", () => {
    const onPublish = vi.fn(async () => undefined);
    const occupiedBlue = {
      definitionId: "definition-1",
      slotName: "blue",
      status: "active" as const,
      publication: {
        publicationId: "publication-blue",
        definitionId: "definition-1",
        versionId: "version-blue",
        artifactId: "artifact-blue",
        slotName: "blue",
        sourceReferenceId: "reference-blue",
        status: "active" as const
      }
    };
    const container = render(review({ slots: [occupiedBlue] }), onPublish);

    flushSync(() => radio(container, "Publish side by side").click());
    setInput(container, "blue");

    expect(container.textContent).toContain("Replace executable artifact-blue");
    expect(container.textContent).toContain("Concurrency protection requires publication publication-blue");

    flushSync(() => button(container, "Review target").click());
    expect(onPublish).toHaveBeenCalledWith({
      action: "sideBySide",
      slotName: "blue",
      expectedPublicationId: "publication-blue"
    });
  });

  it("blocks side-by-side publication to the reserved default slot", () => {
    const onPublish = vi.fn(async () => undefined);
    const container = render(review(), onPublish);

    flushSync(() => radio(container, "Publish side by side").click());

    expect(container.textContent).toContain("The default slot is reserved for replacement publication");
    expect(button(container, "Review target").disabled).toBe(true);
    flushSync(() => button(container, "Review target").click());
    expect(onPublish).not.toHaveBeenCalled();
  });
});

function render(
  value: PublicationReviewState,
  onPublish: (intent: PublicationIntent) => Promise<void>,
  busy = false
) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <PublicationReviewDialog
      review={value}
      busy={busy}
      onPublish={onPublish}
      onCancel={() => undefined}
    />));
  return container;
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

function preflight(overrides: Partial<PublicationPreflight> = {}): PublicationPreflight {
  return {
    preflightToken: "preflight-token-1",
    candidateHash: "candidate-hash-1",
    definitionId: "definition-1",
    versionId: null,
    slotName: "default",
    resolvedAction: "replace",
    policySource: "host",
    canActivate: true,
    claims: [],
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

function setInput(container: HTMLElement, value: string) {
  const input = container.querySelector<HTMLInputElement>("input[aria-label='Publication slot']")!;
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
