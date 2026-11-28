import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SlotEmptyState } from "../workflow-editor/SlotEmptyState";
import type { ActivityCatalogItem } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function click(el: Element) {
  flushSync(() => el.dispatchEvent(new MouseEvent("click", { bubbles: true, clientX: 40, clientY: 60 })));
}

const writeLine: ActivityCatalogItem = {
  activityVersionId: "activity-write-line-v1",
  activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
  version: "1.0.0",
  category: "Primitives",
  displayName: "Write Line",
  description: null,
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: []
};

const flowchartActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-flowchart-v1",
  activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
  category: "Composition",
  displayName: "Flowchart"
};

const sequenceActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-sequence-v1",
  activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence",
  category: "Composition",
  displayName: "Sequence"
};

const catalog = [writeLine, flowchartActivity, sequenceActivity];

function findButton(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button")).find(button => button.textContent?.includes(text));
}

describe("SlotEmptyState", () => {
  it("renders the slot label and the drag-and-drop hint", () => {
    const container = render(
      <SlotEmptyState slotLabel="Body" catalog={catalog} onPickActivity={vi.fn()} onBrowseAll={vi.fn()} />
    );
    expect(container.textContent).toContain("Body");
    expect(container.textContent).toContain("drag activities from the palette");
  });

  it("picks the Flowchart catalog activity when the Flowchart card is clicked", () => {
    const onPickActivity = vi.fn();
    const container = render(
      <SlotEmptyState slotLabel="Body" catalog={catalog} onPickActivity={onPickActivity} onBrowseAll={vi.fn()} />
    );
    click(container.querySelector('[aria-label="Flowchart"]')!);
    expect(onPickActivity).toHaveBeenCalledTimes(1);
    expect(onPickActivity.mock.calls[0][0]).toMatchObject({ activityVersionId: flowchartActivity.activityVersionId });
  });

  it("picks the Sequence catalog activity when the Sequence card is clicked", () => {
    const onPickActivity = vi.fn();
    const container = render(
      <SlotEmptyState slotLabel="Body" catalog={catalog} onPickActivity={onPickActivity} onBrowseAll={vi.fn()} />
    );
    click(container.querySelector('[aria-label="Sequence"]')!);
    expect(onPickActivity.mock.calls[0][0]).toMatchObject({ activityVersionId: sequenceActivity.activityVersionId });
  });

  it("opens the browse-all menu at the click anchor", () => {
    const onBrowseAll = vi.fn();
    const container = render(
      <SlotEmptyState slotLabel="Body" catalog={catalog} onPickActivity={vi.fn()} onBrowseAll={onBrowseAll} />
    );
    click(findButton(container, "Browse all activities")!);
    expect(onBrowseAll).toHaveBeenCalledWith({ clientX: 40, clientY: 60 });
  });

  it("does not fire onPickActivity when the requested container kind is absent from the catalog", () => {
    const onPickActivity = vi.fn();
    // Catalog without a Flowchart or Sequence activity.
    const container = render(
      <SlotEmptyState slotLabel="Body" catalog={[writeLine]} onPickActivity={onPickActivity} onBrowseAll={vi.fn()} />
    );
    click(container.querySelector('[aria-label="Flowchart"]')!);
    expect(onPickActivity).not.toHaveBeenCalled();
  });
});
