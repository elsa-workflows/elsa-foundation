import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityPropertyEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { createObjectExpressionEditorContribution } from "../objectExpressionEditor";
import { activityInputOptionsDescriptor } from "./fixtures/activityInputOptions";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  vi.useFakeTimers();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

const editor: StudioActivityPropertyEditorContribution = {
  id: "test.options",
  supports: descriptor => !!descriptor.uiSpecifications?.optionsProvider,
  component: ({ descriptor, disabled }) => (
    <select aria-label={descriptor.name} disabled={disabled}>
      {(descriptor.uiSpecifications?.options ?? []).map(option => (
        <option key={String(option.value)} value={String(option.value)}>{option.label}</option>
      ))}
    </select>
  )
};

function activity(entity: string, entitySyntax = "Literal"): ActivityNode {
  return {
    nodeId: "node-1",
    activityVersionId: "activity-v1",
    inputs: [],
    outputs: [],
    entity: { typeName: "System.String", expression: { type: entitySyntax, value: entity } }
  };
}

function state(node: ActivityNode): WorkflowDefinitionState {
  return { variables: [], rootActivity: node, inputs: [], outputs: [] };
}

function renderPanel(context: StudioEndpointContext, node: ActivityNode, objectCollection = false) {
  const descriptor = activityInputOptionsDescriptor({
    name: "Field",
    typeName: objectCollection ? "System.Collections.Generic.ICollection`1[System.String]" : "System.String",
    uiSpecifications: { optionsProvider: { key: "catalog.fields", dependsOn: ["Entity"] } }
  });
  descriptor.inputs.unshift({ name: "Entity", typeName: "System.String", isWrapped: true });
  flushSync(() => root.render(
    <ActivityPropertiesPanel
      context={context}
      workflowState={state(node)}
      activity={node}
      descriptor={descriptor}
      editors={[editor]}
      expressionEditors={objectCollection ? [createObjectExpressionEditorContribution(() => [editor])] : []}
      expressionDescriptors={[
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        ...(objectCollection ? [{ type: "Object", displayName: "Object", editingMode: "structured" } as const] : [])
      ]}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      visibleVariables={[]}
      scopeStatus="ready"
      onChange={vi.fn()}
    />
  ));
}

describe("dynamic activity input options", () => {
  it("distinguishes unresolved provider options from a resolved empty result", async () => {
    let resolveRequest!: (value: unknown) => void;
    const postJson = vi.fn(() => new Promise(resolve => { resolveRequest = resolve; }));
    const context = { http: { postJson } } as unknown as StudioEndpointContext;

    renderPanel(context, activity("Customer"));
    await vi.advanceTimersByTimeAsync(0);
    expect(container.textContent).toContain("Loading options...");
    expect(container.textContent).not.toContain("No options available.");

    resolveRequest({ options: [] });
    await vi.waitFor(() => expect(container.textContent).toContain("No options available."));
    expect(container.textContent).not.toContain("Loading options...");
  });

  it("loads on open, debounces dependency refresh, and cancels the superseded request", async () => {
    const requests: Array<{ signal: AbortSignal; resolve(value: unknown): void }> = [];
    const postJson = vi.fn((_url, _body, init) => new Promise(resolve => requests.push({ signal: init.signal, resolve })));
    const context = { http: { postJson } } as unknown as StudioEndpointContext;

    renderPanel(context, activity("Customer"));
    await vi.advanceTimersByTimeAsync(0);
    expect(requests).toHaveLength(1);
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(true);

    renderPanel(context, activity("Order"));
    expect(requests[0].signal.aborted).toBe(true);
    await vi.advanceTimersByTimeAsync(149);
    expect(requests).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(requests).toHaveLength(2);

    requests[1].resolve({ options: [{ label: "Order number", value: "number" }] });
    await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(false));
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Field']")!;
    expect(select.textContent).toContain("Order number");
  });

  it("ignores a superseded request that resolves after the current request", async () => {
    const requests: Array<{ resolve(value: unknown): void }> = [];
    const postJson = vi.fn(() => new Promise(resolve => requests.push({ resolve })));
    const context = { http: { postJson } } as unknown as StudioEndpointContext;

    renderPanel(context, activity("Customer"));
    await vi.advanceTimersByTimeAsync(0);
    renderPanel(context, activity("Order"));
    await vi.advanceTimersByTimeAsync(150);

    requests[1].resolve({ options: [{ label: "Order number", value: "orderNumber" }] });
    await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.textContent).toContain("Order number"));
    requests[0].resolve({ options: [{ label: "Customer name", value: "customerName" }] });
    await Promise.resolve();
    await Promise.resolve();

    const text = container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.textContent;
    expect(text).toContain("Order number");
    expect(text).not.toContain("Customer name");
  });

  it("refreshes when only a dependency's wrapped expression syntax changes", async () => {
    const postJson = vi.fn(async () => ({ options: [{ label: "Name", value: "name" }] }));
    const context = { http: { postJson } } as unknown as StudioEndpointContext;

    renderPanel(context, activity("Customer", "Literal"));
    await vi.advanceTimersByTimeAsync(0);
    await vi.waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));

    renderPanel(context, activity("Customer", "JavaScript"));
    await vi.advanceTimersByTimeAsync(149);
    expect(postJson).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    await vi.waitFor(() => expect(postJson).toHaveBeenCalledTimes(2));
  });

  it("keeps the constrained editor disabled on failure and retries immediately", async () => {
    const postJson = vi.fn()
      .mockRejectedValueOnce(new Error("provider failed"))
      .mockResolvedValueOnce({ options: [{ label: "Name", value: "name" }] });
    const context = { http: { postJson } } as unknown as StudioEndpointContext;

    renderPanel(context, activity("Customer"));
    await vi.advanceTimersByTimeAsync(0);
    await vi.waitFor(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Options are unavailable"));
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(true);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-property-options-error button")!.click());
    await vi.advanceTimersByTimeAsync(0);
    await vi.waitFor(() => expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(false));
    expect(postJson).toHaveBeenCalledTimes(2);
    expect(container.querySelector("[role='alert']")).toBeNull();
  });

  it("keeps an Object collection Contribution disabled while dynamic options load or fail", async () => {
    const postJson = vi.fn(() => new Promise((_resolve, reject) => reject(new Error("provider failed"))));
    const context = { http: { postJson } } as unknown as StudioEndpointContext;
    const node = {
      ...activity("Customer"),
      field: {
        typeName: "System.Collections.Generic.ICollection`1[System.String]",
        expression: { type: "Object", value: ["existing"] }
      }
    };

    renderPanel(context, node, true);
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(true);

    await vi.advanceTimersByTimeAsync(0);
    await vi.waitFor(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Options are unavailable"));
    expect(container.querySelector<HTMLSelectElement>("select[aria-label='Field']")?.disabled).toBe(true);
  });
});
