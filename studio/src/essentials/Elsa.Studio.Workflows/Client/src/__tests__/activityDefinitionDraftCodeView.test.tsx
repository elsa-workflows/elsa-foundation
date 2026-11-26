import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";
import { ActivityDefinitionDraftCodeView } from "../ActivityDefinitionDraftCodeView";

interface RenderedCodeView {
  container: HTMLDivElement;
  textarea: HTMLTextAreaElement;
  unmount(): void;
}

const mounted: RenderedCodeView[] = [];

afterEach(() => {
  for (const item of mounted.splice(0)) item.unmount();
});

describe("ActivityDefinitionDraftCodeView", () => {
  it("keeps invalid JSON local and disables Apply", async () => {
    const onApply = vi.fn();
    const onBufferStateChange = vi.fn();
    const rendered = renderCodeView({ onApply, onBufferStateChange });

    change(rendered.textarea, "{ invalid");

    await vi.waitFor(() => expect(onBufferStateChange).toHaveBeenLastCalledWith({ dirty: true, valid: false }));
    expect(rendered.container.querySelector(".studio-code-editor-diagnostics")?.textContent)
      .toContain("must be valid JSON");
    expect(buttonByText(rendered.container, "Apply").disabled).toBe(true);
    expect(onApply).not.toHaveBeenCalled();
  });

  it("applies the editable authoring projection through one host change", () => {
    const onApply = vi.fn();
    const rendered = renderCodeView({ onApply });
    const projection = JSON.parse(rendered.textarea.value) as Record<string, unknown>;
    projection.presentationLabel = "Reviewed graph";

    change(rendered.textarea, JSON.stringify(projection, null, 2));
    click(buttonByText(rendered.container, "Apply"));

    expect(onApply).toHaveBeenCalledTimes(1);
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({
      definitionId: "definition-1",
      draftId: "draft-1",
      revision: 7,
      presentationLabel: "Reviewed graph",
      provider: expect.objectContaining({
        providerKey: "elsa.activity-graph",
        schemaVersion: "2"
      })
    }));
  });

  it("exposes applied JSON as one host-managed undo and redo operation", () => {
    const onUndo = vi.fn();
    const onRedo = vi.fn();
    const rendered = renderCodeView({
      canUndo: true,
      canRedo: true,
      onUndo,
      onRedo
    });

    click(buttonByText(rendered.container, "Undo Apply"));
    click(buttonByText(rendered.container, "Redo Apply"));

    expect(onUndo).toHaveBeenCalledTimes(1);
    expect(onRedo).toHaveBeenCalledTimes(1);
  });

  it("resets an unapplied buffer without emitting a host edit", async () => {
    const onApply = vi.fn();
    const onBufferStateChange = vi.fn();
    const rendered = renderCodeView({ onApply, onBufferStateChange });
    const baseline = rendered.textarea.value;
    const projection = JSON.parse(baseline) as Record<string, unknown>;
    projection.presentationLabel = "Discard me";

    change(rendered.textarea, JSON.stringify(projection));
    click(buttonByText(rendered.container, "Reset"));

    expect(rendered.textarea.value).toBe(baseline);
    await vi.waitFor(() => expect(onBufferStateChange).toHaveBeenLastCalledWith({ dirty: false, valid: true }));
    expect(onApply).not.toHaveBeenCalled();
  });
});

function renderCodeView({
  draft = createDraft(),
  canUndo = false,
  canRedo = false,
  onApply = vi.fn(),
  onUndo = vi.fn(),
  onRedo = vi.fn(),
  onBufferStateChange = vi.fn()
}: {
  draft?: ActivityDefinitionDraftView;
  canUndo?: boolean;
  canRedo?: boolean;
  onApply?(draft: ActivityDefinitionDraftView): void;
  onUndo?(): void;
  onRedo?(): void;
  onBufferStateChange?(state: { dirty: boolean; valid: boolean }): void;
} = {}): RenderedCodeView {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(
    <ActivityDefinitionDraftCodeView
      draft={draft}
      readOnly={false}
      canUndo={canUndo}
      canRedo={canRedo}
      onApply={onApply}
      onUndo={onUndo}
      onRedo={onRedo}
      onBufferStateChange={onBufferStateChange}
    />
  ));
  const textarea = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Activity Definition JSON']");
  if (!textarea) throw new Error("Activity Definition JSON textarea was not rendered.");
  const rendered = {
    container,
    textarea,
    unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
  mounted.push(rendered);
  return rendered;
}

function createDraft(): ActivityDefinitionDraftView {
  return {
    definitionId: "definition-1",
    draftId: "draft-1",
    tenantId: "tenant-1",
    revision: 7,
    sourceVersionId: null,
    status: "active",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }]
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "2",
      manifestFingerprint: "sha256:test",
      payload: {
        rootActivity: {
          nodeId: "root",
          activityVersionId: "flowchart",
          inputs: [],
          outputs: [],
          structure: { kind: "flowchart", activities: [] }
        },
        variables: [],
        outputMappings: [],
        outcomeMappings: []
      }
    },
    layout: [],
    validation: null,
    createdAt: "2026-07-28T00:00:00Z",
    updatedAt: "2026-07-28T00:00:00Z",
    presentationLabel: null
  };
}

function change(element: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(candidate => candidate.textContent?.trim() === text);
  if (!button) throw new Error(`Button '${text}' was not rendered.`);
  return button;
}
