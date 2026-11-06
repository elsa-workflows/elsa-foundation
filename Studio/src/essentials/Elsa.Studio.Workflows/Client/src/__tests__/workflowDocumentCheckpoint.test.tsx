import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { WorkflowDraft } from "../workflowTypes";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";

const mounted: { root: Root; element: HTMLElement }[] = [];
afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.element.remove();
  }
});

function draft(name = "Before"): WorkflowDraft {
  return { id: "draft", definitionId: "definition", layout: [], validationErrors: [],
    state: { rootActivity: { nodeId: "process", activityVersionId: "exact", inputs: [], outputs: [], displayName: name } } };
}

function renderDocument() {
  const element = document.createElement("div");
  document.body.appendChild(element);
  const root = createRoot(element);
  let current!: WorkflowDocumentApi;
  function Harness() {
    current = useWorkflowDocument();
    return <output>{current.state.draft?.state.rootActivity?.displayName as string ?? "Empty"}</output>;
  }
  flushSync(() => root.render(<Harness />));
  mounted.push({ root, element });
  return { current: () => current, element };
}

describe("committed Workflow authoring source", () => {
  it("invalidates a module-owned plane/scope without changing persisted content or ordinary history inputs", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    flushSync(() => view.current().invalidateAuthoringScope());
    const after = view.current().readCommitted();
    expect(after.revision).toBe(before.revision + 1);
    expect(after.state).toBe(before.state);
    expect(after.state.draft).toBe(before.state.draft);
  });

  it("detects module-owned scope ABA even when both transitions share one React commit", () => {
    const view = renderDocument();
    const before = view.current().readCommitted();
    flushSync(() => { view.current().invalidateAuthoringScope(); view.current().invalidateAuthoringScope(); });
    expect(view.current().readCommitted().revision).toBe(before.revision + 2);
    expect(view.current().readCommitted().state).toBe(before.state);
  });

  it("exposes the accepted document before a synchronous canvas acknowledgement returns", () => {
    const view = renderDocument();
    const read = view.current().readCommitted;
    expect(read().revision).toBe(0);
    flushSync(() => view.current().loadDraft(draft()));
    expect(read().state.draft).toEqual(draft());
    expect(read().revision).toBe(1);
    flushSync(() => view.current().editDraft(({ draft: current }) => ({ ...current!, state: { ...current!.state,
      rootActivity: { ...current!.state.rootActivity!, displayName: "Accepted" } } })));
    expect(read().state.draft?.state.rootActivity?.displayName).toBe("Accepted");
    expect(read().revision).toBe(2);
    expect(view.current().readCommitted).toBe(read);
  });

  it("detects source ABA even when React batches both edits into one visible commit", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    const change = vi.fn((name: string) => view.current().editDraft(({ draft: current }) => ({ ...current!,
      state: { ...current!.state, rootActivity: { ...current!.state.rootActivity!, displayName: name } } })));
    flushSync(() => { change("Between"); change("Before"); });
    const after = view.current().readCommitted();
    expect(after.state.draft).toEqual(before.state.draft);
    expect(after.revision).toBe(before.revision + 2);
    expect(change).toHaveBeenCalledTimes(2);
    expect(view.element.textContent).toBe("Before");
  });

  it("retains source generation when an authoring recipe refuses its change", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    flushSync(() => view.current().editDraft(() => null));
    expect(view.current().readCommitted()).toBe(before);
  });

  it("does not mistake selection, artifact or validation acknowledgements for source edits", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    flushSync(() => {
      view.current().select("task");
      view.current().setPublishedArtifact("artifact");
      view.current().editDraft(({ draft: current }) => ({ ...current!, validationErrors: [{ path: "process", type: "Execution", message: "Not runnable" }] }));
    });
    const after = view.current().readCommitted();
    expect(after.revision).toBe(before.revision);
    expect(after.state.selectedNodeId).toBe("task");
    expect(after.state.publishedArtifactId).toBe("artifact");
    expect(after.state.draft?.validationErrors).toHaveLength(1);
  });

  it("distinguishes a scope round trip from the original canvas generation", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    flushSync(() => {
      view.current().navigateToScope([{ ownerNodeId: "process", slotId: "native-body", label: "Native body" }]);
      view.current().resetToRoot();
    });
    const after = view.current().readCommitted();
    expect(after.state.frames).toEqual(before.state.frames);
    expect(after.revision).toBe(before.revision + 2);
  });

  it("tracks changes to all persisted authoring side tables", () => {
    const view = renderDocument();
    flushSync(() => view.current().loadDraft(draft()));
    const before = view.current().readCommitted();
    flushSync(() => {
      view.current().editDraft(({ draft: current }) => ({ ...current!, layout: [{ nodeId: "process", x: 1.25, y: 2.5 }] }));
      view.current().editDraft(({ draft: current }) => ({ ...current!, activityPresentation: [{ nodeId: "process", displayName: "Presentation" }] }));
    });
    expect(view.current().readCommitted().revision).toBe(before.revision + 2);
    expect(view.current().readCommitted().state.draft?.layout[0].x).toBe(1.25);
  });
});
