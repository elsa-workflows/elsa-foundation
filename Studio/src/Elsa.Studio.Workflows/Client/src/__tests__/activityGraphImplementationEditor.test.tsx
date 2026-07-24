import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDefinitionImplementationEditorProps } from "@elsa-workflows/studio-sdk";
import { ActivityGraphImplementationEditor } from "../ActivityGraphImplementationEditor";

vi.mock("../api/activityDesign", () => ({
  useWorkflowActivities: () => ({
    data: { activities: [rootCatalogItem] },
    isPending: false,
    isError: false
  })
}));

let rootCatalogItem = catalogItem();
const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  rootCatalogItem = catalogItem();
});

describe("ActivityGraphImplementationEditor boundary outcome mappings", () => {
  it("adds a stable root-outcome mapping and preserves existing mappings", () => {
    const onChange = vi.fn();
    const rendered = renderEditor({
      value: implementationValue({
        outcomeMappings: [{ sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" }]
      }),
      onChange
    });

    const source = rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Root outcome reference key']")!;
    const boundary = rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Boundary outcome reference key']")!;
    expect([...source.options].map(option => ({ value: option.value, disabled: option.disabled }))).toContainEqual({ value: "rejected", disabled: false });
    expect([...source.options].map(option => option.value)).not.toContain("unstable-name-only");
    expect([...boundary.options].map(option => ({ value: option.value, disabled: option.disabled }))).toContainEqual({ value: "declined-boundary", disabled: false });

    change(source, "rejected");
    change(boundary, "declined-boundary");
    click(buttonByText(rendered.container, "Add mapping"));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      payload: expect.objectContaining({
        outcomeMappings: [
          { sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" },
          { sourceOutcomeReferenceKey: "rejected", boundaryOutcomeReferenceKey: "declined-boundary" }
        ]
      })
    }));
  });

  it("renders mapping controls as read-only when the draft is locked", () => {
    const rendered = renderEditor({
      readOnly: true,
      value: implementationValue({
        outcomeMappings: [{ sourceOutcomeReferenceKey: "approved", boundaryOutcomeReferenceKey: "approved-boundary" }]
      })
    });

    expect(rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Root outcome reference key']")?.disabled).toBe(true);
    expect(rendered.container.querySelector<HTMLSelectElement>("select[aria-label='Boundary outcome reference key']")?.disabled).toBe(true);
    expect(buttonByText(rendered.container, "Add mapping").disabled).toBe(true);
    expect(buttonByText(rendered.container, "Remove mapping").disabled).toBe(true);
  });

  it("keeps schema 1 free of schema-2 mapping controls", () => {
    const rendered = renderEditor({ providerSchemaVersion: "1" });

    expect(rendered.container.textContent).not.toContain("Boundary outcome mappings");
  });
});

function renderEditor(overrides: Partial<StudioActivityDefinitionImplementationEditorProps> = {}) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const props: StudioActivityDefinitionImplementationEditorProps = {
    context: {} as StudioActivityDefinitionImplementationEditorProps["context"],
    definitionId: "definition-1",
    draftId: "draft-1",
    revision: 1,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "2",
    manifestFingerprint: "sha256:test",
    contract: {
      contractSchemaVersion: "1",
      outcomes: [
        { referenceKey: "approved-boundary", name: "Approved", isEmitted: true },
        { referenceKey: "declined-boundary", name: "Declined", isEmitted: true },
        { referenceKey: "draft-boundary", name: "Draft", isEmitted: false }
      ]
    },
    value: implementationValue(),
    readOnly: false,
    onChange: () => {},
    ...overrides
  };
  flushSync(() => root.render(<ActivityGraphImplementationEditor {...props} />));
  const rendered = { root, container };
  mounted.push(rendered);
  return rendered;
}

function implementationValue(payload: Record<string, unknown> = {}) {
  return {
    payload: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "decision-v1",
        inputs: [],
        outputs: [],
        structure: null
      },
      variables: [],
      outputMappings: [],
      outcomeMappings: [],
      ...payload
    },
    layout: []
  };
}

function catalogItem() {
  return {
    activityVersionId: "decision-v1",
    activityTypeKey: "acme.decision",
    version: "1.0.0",
    category: "Tests",
    displayName: "Decision",
    executionType: "Action",
    inputs: [],
    outputs: [],
    ports: [
      { type: "outcome", referenceKey: "approved", displayName: "Approved" },
      { type: "outcome", referenceKey: "rejected", displayName: "Rejected" },
      { type: "flow", name: "unstable-name-only", displayName: "Unstable" }
    ]
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent?.trim() === text);
  if (!button) throw new Error(`Button not found: ${text}`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function change(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    element.value = value;
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
