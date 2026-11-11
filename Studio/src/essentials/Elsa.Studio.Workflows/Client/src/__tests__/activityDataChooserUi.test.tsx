import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent } from "@elsa-workflows/studio-sdk";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { activityResultReferenceContribution } from "../activityResultReferenceContribution";
import type { ActivityDataSelectionHost, ActivityDataInputFinding } from "../activityDataSelection";
import type { ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataTarget } from "../api/activityDataSelection";
import type { ActivityNode } from "../workflowTypes";

// Choose data at an ordinary activity input (spec 1215 T020/T021/T023, whole outputs): the panel stores the server's
// binding unchanged as one edit, explains what cannot be chosen and keeps every protected input's own contract.

const descriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" }
];

const textEditor: StudioActivityPropertyEditorContribution = {
  // The host's general single-line editor identity: an input it edits offers Choose data.
  id: "studio.property.singleline",
  supports: descriptor => !descriptor.uiHint?.startsWith("module-"),
  component: ({ descriptor, value, disabled, onChange }) => (
    <input aria-label={descriptor.name} value={String(value ?? "")} disabled={disabled} onChange={event => onChange(event.target.value)} />
  )
};

// A module-owned control, such as a compensation target or user-task assignee picker, keeps its own source contract.
const moduleEditor: StudioActivityPropertyEditorContribution = {
  id: "test.module-assignee",
  supports: descriptor => descriptor.uiHint === "module-assignee",
  component: ({ descriptor }) => <select aria-label={descriptor.name} />
};

const customerBinding = { expressionType: "ActivityResult", value: { producerNodeId: "get-customer", projectionKey: "$result", isOptional: false } };
const orderBinding = { expressionType: "ActivityResult", value: { producerNodeId: "get-order", projectionKey: "$result", isOptional: false } };

const sources: ActivityDataSourceFacts[] = [
  source("activity:get-customer:$result", customerBinding, null, "Customer"),
  source("activity:get-order:$result", orderBinding, null, null),
  source("activity:loop-step:$result", null, "EarlierProducerRun", null),
  // Another role the server reports: the activity-output chooser leaves it to its own reference editor.
  { id: "input:order-id", displayName: "Order ID", binding: { expressionType: "Input", value: { referenceKey: "order-id" } },
    reasonCode: null, availability: "definite", activityResultSource: null, type: null }
];

function source(id: string, binding: ActivityDataSourceFacts["binding"], reasonCode: string | null, alias: string | null): ActivityDataSourceFacts {
  const producerNodeId = id.split(":")[1]!;
  return {
    id,
    displayName: producerNodeId,
    binding,
    reasonCode,
    availability: reasonCode ? "definite" : "definite",
    activityResultSource: { producerNodeId, projectionKey: "$result" },
    type: alias ? { alias, collectionKind: "Single" } : null
  };
}

function answer(state: ActivityDataContextResult["state"], overrides: Partial<ActivityDataContextResult> = {}): ActivityDataContextResult {
  return { state, code: null, documentRevision: "rev-1", baseRevision: "base-1", target: null, sources: [], hasMoreSources: false, ...overrides };
}

const labels: Record<string, string> = { "get-customer": "Get customer", "get-order": "Get order", "loop-step": "Loop step" };

function host(overrides: Partial<ActivityDataSelectionHost> = {}): ActivityDataSelectionHost & { requests: ActivityDataTarget[] } {
  const requests: ActivityDataTarget[] = [];
  return {
    documentRevision: "rev-1",
    baseRevision: "base-1",
    requests,
    requestSources: vi.fn(async (target: ActivityDataTarget) => {
      requests.push(target);
      return answer("supported", { sources });
    }),
    describeActivity: nodeId => labels[nodeId] ? { label: labels[nodeId], outputLabels: {} } : null,
    revealActivity: vi.fn(),
    findingsFor: () => [],
    ...overrides
  };
}

let mounted: { root: Root; container: HTMLElement } | null = null;
let setHost: (next: ActivityDataSelectionHost | null) => void = () => undefined;

afterEach(() => {
  if (mounted) {
    const current = mounted;
    flushSync(() => current.root.unmount());
    current.container.remove();
    mounted = null;
  }
});

function input(name: string, overrides: Partial<StudioActivityInputDescriptor> = {}): StudioActivityInputDescriptor {
  return { name, displayName: name, referenceKey: name.charAt(0).toLowerCase() + name.slice(1), typeName: "System.String", isWrapped: true, ...overrides };
}

function render(options: {
  inputs?: StudioActivityInputDescriptor[];
  activity?: ActivityNode;
  dataHost?: ActivityDataSelectionHost | null;
  readOnly?: boolean;
  onChange?(activity: ActivityNode): void;
} = {}) {
  const container = document.createElement("div");
  container.className = "wf-tokens";
  document.body.appendChild(container);
  const root = createRoot(container);
  const descriptor: StudioActivityDescriptor = {
    typeName: "CreateInvoice",
    inputs: options.inputs ?? [input("CustomerId", { displayName: "Customer ID" })],
    outputs: [],
    ports: []
  };
  function Harness() {
    const [current, setCurrent] = React.useState<ActivityNode>(options.activity ?? { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [] });
    const [dataHost, setDataHost] = React.useState<ActivityDataSelectionHost | null>(options.dataHost === undefined ? host() : options.dataHost);
    setHost = setDataHost;
    return (
      <ActivityPropertiesPanel
        activity={current}
        descriptor={descriptor}
        editors={[textEditor, moduleEditor]}
        expressionEditors={[activityResultReferenceContribution]}
        expressionDescriptors={descriptors}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        readOnly={options.readOnly}
        activityDataSelection={dataHost}
        onChange={next => {
          options.onChange?.(next);
          setCurrent(next);
        }}
      />
    );
  }
  flushSync(() => root.render(<Harness />));
  mounted = { root, container };
  return container;
}

async function settle() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
  await Promise.resolve();
}

function trigger(container: HTMLElement) {
  return container.querySelector<HTMLButtonElement>(".wf-choose-data-trigger");
}

function options() {
  return [...document.querySelectorAll<HTMLButtonElement>(".wf-data-chooser [role='option']")];
}

function option(label: string, group: string) {
  return options().find(candidate =>
    candidate.closest("[role='group']")?.querySelector(".wf-data-chooser-group-label")?.textContent === group &&
    candidate.querySelector(".wf-data-chooser-option-label")?.textContent === label);
}

async function openChooser(container: HTMLElement) {
  flushSync(() => trigger(container)!.click());
  await vi.waitFor(() => expect(document.querySelector(".wf-data-chooser")).not.toBeNull());
}

function chooser() {
  return document.querySelector<HTMLElement>(".wf-data-chooser-panel");
}

describe("Choose data for an activity input", () => {
  it("stores the server's binding unchanged as one edit on an untouched input", async () => {
    const changes: ActivityNode[] = [];
    const dataHost = host();
    const container = render({ dataHost, onChange: next => changes.push(next) });

    expect(trigger(container)?.getAttribute("aria-label")).toBe("Choose data for Customer ID");
    await openChooser(container);
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
    expect(dataHost.requests).toEqual([{ nodeId: "node-1", inputKey: "customerId" }]);
    // Only activity outputs are offered; the reported workflow input keeps its own editor.
    expect(chooser()?.textContent).not.toContain("Order ID");

    flushSync(() => option("Result", "Get customer")!.click());

    expect(changes).toHaveLength(1);
    const stored = changes[0]!.customerId as { expression: { type: string; value: unknown } };
    expect(stored.expression.type).toBe("ActivityResult");
    expect(stored.expression.value).toBe(customerBinding.value);
    expect(stored.expression.value).toEqual({ producerNodeId: "get-customer", projectionKey: "$result", isOptional: false });
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    await settle();
    expect(container.querySelector(".wf-data-reference-source")?.textContent).toBe("Data from Get customer › , output Result");
    expect(document.activeElement).toBe(container.querySelector(".wf-data-reference-replace"));
    // Choosing data opens no fixed-value chrome and needs no save: only the source request ran.
    expect(dataHost.requestSources).toHaveBeenCalledTimes(1);
  });

  it("shows sources that can't be chosen with their reason and never selects them", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ onChange: next => changes.push(next) });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Result", "Loop step")).toBeDefined());

    const blocked = option("Result", "Loop step")!;
    expect(blocked.getAttribute("aria-disabled")).toBe("true");
    const reason = document.getElementById(blocked.getAttribute("aria-describedby")!);
    expect(reason?.textContent).toContain("more than once");
    flushSync(() => blocked.click());
    flushSync(() => blocked.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(changes).toHaveLength(0);
    expect(document.querySelector(".wf-data-chooser")).not.toBeNull();
  });

  it("keeps two activities with the same name distinguishable by their identity", async () => {
    const changes: ActivityNode[] = [];
    const container = render({
      onChange: next => changes.push(next),
      dataHost: host({ describeActivity: nodeId => labels[nodeId] ? { label: "Timer", outputLabels: {} } : null })
    });
    await openChooser(container);
    await vi.waitFor(() => expect(document.querySelectorAll(".wf-data-chooser-group-label")).toHaveLength(3));
    expect([...document.querySelectorAll(".wf-data-chooser-group-label")].map(label => label.textContent))
      .toEqual(["Timer · get-customer", "Timer · get-order", "Timer · loop-step"]);
    const order = document.querySelector<HTMLElement>("[role='group'][aria-labelledby]:nth-child(2) [role='option']")!;
    flushSync(() => order.click());
    expect((changes[0]!.customerId as { expression: { value: unknown } }).expression.value).toBe(orderBinding.value);
  });

  it("operates by keyboard and returns focus to Choose data when cancelled", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ onChange: next => changes.push(next) });
    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Result", "Get customer")));

    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    expect(document.activeElement).toBe(option("Result", "Get order"));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true })));
    expect(document.activeElement).toBe(option("Result", "Loop step"));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));

    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(document.activeElement).toBe(trigger(container));
    expect(changes).toHaveLength(0);

    // Tab leaves the browser the same way, so focus never falls to the end of the document.
    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Result", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true })));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(document.activeElement).toBe(trigger(container));

    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Result", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(changes).toHaveLength(1);
    expect((changes[0]!.customerId as { expression: { value: unknown } }).expression.value).toBe(orderBinding.value);
  });

  it.each([
    ["denied", answer("denied", { code: "activity-data.denied" }), "don't have access", false],
    ["policy-unavailable", answer("unavailable", { code: "activity-data.metadata-policy-unavailable" }), "restricts data browsing", false],
    ["analysis-unavailable", answer("unavailable", { code: "activity-data.analysis-unavailable" }), "aren't available right now", true],
    ["stale elsewhere", answer("stale", { code: "activity-data.base-changed" }), "saved somewhere else", true],
    ["incompatible", answer("incompatible", { code: "activity-data.response-unreadable" }), "can't read", false],
    ["invalid target", answer("invalid", { code: "activity-data.invalid-source-or-target" }), "can't take data", false],
    ["empty", answer("empty"), "No earlier activity outputs", false],
    ["only other roles", answer("supported", { sources: [sources[3]!] }), "No earlier activity outputs", false],
    ["constrained", answer("supported", { sources, target: { displayName: "Customer ID", constraint: {
      owner: "Credential", allowedExpressionTypes: ["Secret"], message: "A credential input accepts only a secret reference." } } }), "accepts only a secret reference", false]
  ])("explains the %s state without offering a source", async (_name, result, text, retry) => {
    const container = render({ dataHost: host({ requestSources: vi.fn(async () => result) }) });
    await openChooser(container);
    await vi.waitFor(() => expect(chooser()?.textContent).toContain(text));
    expect(options()).toHaveLength(0);
    expect([...chooser()!.querySelectorAll("button")].some(button => button.textContent === "Retry")).toBe(retry);
  });

  it("shows loading, then a failure with Retry that asks again", async () => {
    let fail = true;
    let release!: () => void;
    const requestSources = vi.fn(() => new Promise<ActivityDataContextResult>((resolve, reject) => {
      release = () => (fail ? reject(new Error("offline")) : resolve(answer("supported", { sources })));
    }));
    const container = render({ dataHost: host({ requestSources }) });
    await openChooser(container);
    expect(chooser()?.querySelector("[aria-label='Loading data sources']")).not.toBeNull();

    release();
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("couldn't be loaded"));
    const retry = [...chooser()!.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Retry")!;
    fail = false;
    flushSync(() => retry.click());
    expect(requestSources).toHaveBeenCalledTimes(2);
    release();
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
  });

  it("drops an answer for an older document and asks again for the current one", async () => {
    const pending: Array<{ signal: AbortSignal; resolve(result: ActivityDataContextResult): void }> = [];
    const requestSources = vi.fn((_target: ActivityDataTarget, signal: AbortSignal) =>
      new Promise<ActivityDataContextResult>(resolve => pending.push({ signal, resolve })));
    const first = host({ requestSources });
    const container = render({ dataHost: first });
    await openChooser(container);
    expect(pending).toHaveLength(1);

    // An edit elsewhere in the document changes its revision while the first request is in flight.
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    expect(pending).toHaveLength(2);
    expect(pending[0]!.signal.aborted).toBe(true);
    pending[0]!.resolve(answer("supported", { sources }));
    await settle();
    expect(options()).toHaveLength(0);

    pending[1]!.resolve(answer("empty", { documentRevision: "rev-2" }));
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("No earlier activity outputs"));
  });

  it("keeps the list in place while a newer revision is asked about, storing a choice made meanwhile from the new answer", async () => {
    const changes: ActivityNode[] = [];
    const pending: Array<(result: ActivityDataContextResult) => void> = [];
    const requestSources = vi.fn(() => new Promise<ActivityDataContextResult>(resolve => pending.push(resolve)));
    const first = host({ requestSources });
    const container = render({ dataHost: first, onChange: next => changes.push(next) });
    await openChooser(container);
    pending[0]!(answer("supported", { sources }));
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Result", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    const focused = document.activeElement;

    // A save that only normalized the document changes its revision while the author is choosing.
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    expect(pending).toHaveLength(2);
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")?.getAttribute("aria-busy")).toBe("true"));
    expect(document.activeElement).toBe(focused);
    expect(chooser()?.textContent).toContain("Updating for your latest changes");
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(changes).toHaveLength(0);
    expect(option("Result", "Get order")?.getAttribute("aria-busy")).toBe("true");

    // The newer answer carries a newer binding for the same source; that one is stored, never the older one.
    const fresh = { expressionType: "ActivityResult", value: { producerNodeId: "get-order", projectionKey: "$result", isOptional: false } };
    pending[1]!(answer("supported", { documentRevision: "rev-2", sources: [sources[0]!, { ...sources[1]!, binding: fresh }] }));
    await vi.waitFor(() => expect(changes).toHaveLength(1));
    expect((changes[0]!.customerId as { expression: { value: unknown } }).expression.value).toBe(fresh.value);
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
  });

  it("does not store a choice made meanwhile when the newer answer no longer offers it", async () => {
    const changes: ActivityNode[] = [];
    const pending: Array<(result: ActivityDataContextResult) => void> = [];
    const first = host({ requestSources: vi.fn(() => new Promise<ActivityDataContextResult>(resolve => pending.push(resolve))) });
    const container = render({ dataHost: first, onChange: next => changes.push(next) });
    await openChooser(container);
    pending[0]!(answer("supported", { sources }));
    await vi.waitFor(() => expect(option("Result", "Get order")).toBeDefined());
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")?.getAttribute("aria-busy")).toBe("true"));
    flushSync(() => option("Result", "Get order")!.click());

    pending[1]!(answer("supported", { documentRevision: "rev-2", sources: [sources[0]!, { ...sources[1]!, binding: null, reasonCode: "ConditionalOccurrence" }] }));
    await vi.waitFor(() => expect(option("Result", "Get order")?.getAttribute("aria-disabled")).toBe("true"));
    await settle();
    expect(changes).toHaveLength(0);
    expect(document.querySelector(".wf-data-chooser")).not.toBeNull();
  });

  it("asks again once a save it was waiting for is acknowledged", async () => {
    const requestSources = vi.fn()
      .mockResolvedValueOnce(answer("stale", { code: "activity-data.base-changed" }))
      .mockResolvedValueOnce(answer("supported", { sources }));
    const first = host({ requestSources });
    const container = render({ dataHost: first });
    await openChooser(container);
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("saved somewhere else"));

    flushSync(() => setHost({ ...first, baseRevision: "base-2" }));
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
    expect(requestSources).toHaveBeenCalledTimes(2);
  });

  it("closes and forgets sources when the sign-in session ends", async () => {
    const container = render();
    await openChooser(container);
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
    flushSync(() => window.dispatchEvent(new CustomEvent(authSessionEndedEvent)));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
  });

  it("asks before replacing an authored value and keeps it on cancel", async () => {
    const changes: ActivityNode[] = [];
    const container = render({
      onChange: next => changes.push(next),
      activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
        customerId: { typeName: "System.String", expression: { type: "Literal", value: "C-42" } } }
    });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
    flushSync(() => option("Result", "Get customer")!.click());

    expect(changes).toHaveLength(0);
    const confirmation = container.querySelector<HTMLElement>("[role='alertdialog']")!;
    expect(confirmation.textContent).toContain("Activity output will replace the current value");
    const cancel = [...confirmation.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    flushSync(() => cancel.click());
    expect(changes).toHaveLength(0);

    await openChooser(container);
    await vi.waitFor(() => expect(option("Result", "Get customer")).toBeDefined());
    flushSync(() => option("Result", "Get customer")!.click());
    const replace = [...container.querySelectorAll<HTMLButtonElement>("[role='alertdialog'] button")].find(button => button.textContent === "Replace value")!;
    flushSync(() => replace.click());
    expect(changes).toHaveLength(1);
    expect((changes[0]!.customerId as { expression: { type: string; value: unknown } }).expression).toEqual({
      type: "ActivityResult", value: customerBinding.value
    });
  });

  it.each([
    ["a secret-only input", input("ApiKey", { isCredential: true, isSensitive: true })],
    ["a sensitive input", input("Token", { isSensitive: true })],
    ["a password input", input("Password", { uiHint: "password" })],
    ["a connection input", input("Connection", { connectionType: "Smtp" })],
    ["an options-provider input", input("Field", { uiSpecifications: { optionsProvider: { key: "catalog.fields", dependsOn: [] } } })],
    ["a read-only input", input("Fixed", { isReadOnly: true })],
    ["an unwrapped input", input("Literal", { isWrapped: false })],
    ["an input a module edits with its own control", input("Assignee", { uiHint: "module-assignee" })]
  ])("does not offer Choose data on %s", (_name, protectedInput) => {
    const container = render({ inputs: [protectedInput] });
    expect(trigger(container)).toBeNull();
  });

  it("offers nothing on a read-only document or without the owner's source analysis", () => {
    expect(trigger(render({ readOnly: true }))).toBeNull();
    afterEachUnmount();
    expect(trigger(render({ dataHost: null }))).toBeNull();
  });
});

describe("a stored activity output", () => {
  const legacyValue = { producerNodeId: "get-customer", projectionKey: "$result", producerScopeId: "scope:root", isOptional: false };

  function storedActivity(value: unknown): ActivityNode {
    return {
      nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "ActivityResult", value } }
    };
  }

  it("renders a legacy scoped binding as a readable reference without rewriting it", async () => {
    const changes: ActivityNode[] = [];
    const activity = storedActivity(legacyValue);
    const dataHost = host();
    const container = render({ activity, dataHost, onChange: next => changes.push(next) });
    await settle();

    expect(container.querySelector(".wf-data-reference-source")?.textContent).toContain("Get customer");
    expect(container.querySelector(".wf-syntax-picker-trigger")?.getAttribute("aria-label")).toBe("Customer ID expression syntax: Activity output");
    // A stored reference offers Replace, not a second Choose data action, and nothing is requested until asked.
    expect(trigger(container)).toBeNull();
    expect(dataHost.requestSources).not.toHaveBeenCalled();
    expect(changes).toHaveLength(0);
    expect((activity.customerId as { expression: { value: unknown } }).expression.value).toBe(legacyValue);

    const goTo = [...container.querySelectorAll<HTMLButtonElement>(".wf-data-reference-actions button")].find(button => button.textContent?.includes("Go to"))!;
    expect(goTo.getAttribute("aria-label")).toBe("Go to Get customer");
    flushSync(() => goTo.click());
    expect(dataHost.revealActivity).toHaveBeenCalledWith("get-customer");
    expect(changes).toHaveLength(0);
  });

  it("replaces it with the newly chosen binding, marking the current source", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ activity: storedActivity(legacyValue), onChange: next => changes.push(next) });
    await settle();
    const replace = container.querySelector<HTMLButtonElement>(".wf-data-reference-replace")!;
    flushSync(() => replace.click());
    await vi.waitFor(() => expect(option("Result", "Get customer")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Result", "Get customer")));

    flushSync(() => option("Result", "Get order")!.click());
    expect(changes).toHaveLength(1);
    expect((changes[0]!.customerId as { expression: { type: string; value: unknown } }).expression).toEqual({
      type: "ActivityResult", value: orderBinding.value
    });
    await settle();
    expect(document.activeElement).toBe(container.querySelector(".wf-data-reference-replace"));
  });

  it("clears it in one edit, leaving the input untouched", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ activity: storedActivity(legacyValue), onChange: next => changes.push(next) });
    await settle();
    const clear = [...container.querySelectorAll<HTMLButtonElement>(".wf-data-reference-actions button")].find(button => button.textContent === "Clear")!;
    flushSync(() => clear.click());

    expect(changes).toHaveLength(1);
    expect(Object.prototype.hasOwnProperty.call(changes[0], "customerId")).toBe(false);
    await settle();
    expect(container.querySelector(".wf-data-reference")).toBeNull();
    expect(document.activeElement).toBe(trigger(container));
  });

  it("explains a missing source activity and keeps replace and clear working", async () => {
    const container = render({ activity: storedActivity({ producerNodeId: "deleted-step", projectionKey: "$result", isOptional: false }) });
    await settle();
    const reference = container.querySelector(".wf-data-reference")!;
    expect(reference.textContent).toContain("no longer in this workflow");
    expect(reference.textContent).toContain("deleted-step");
    expect([...reference.querySelectorAll("button")].map(button => button.textContent)).toEqual(["Replace", "Clear"]);
  });

  it("shows the server's finding for the input it describes", async () => {
    const findings: ActivityDataInputFinding[] = [{ type: "ValueFlow/EarlierProducerRun", message: "server text" }];
    const findingsFor = vi.fn(() => findings);
    const container = render({ activity: storedActivity(legacyValue), dataHost: host({ findingsFor }) });
    await settle();
    expect(findingsFor).toHaveBeenCalledWith({ nodeId: "node-1", inputKey: "customerId" });
    expect(container.querySelector(".wf-data-reference-problem")?.textContent).toContain("can run more than once");
  });

  it("stays readable and preserved where no source analysis is offered", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ activity: storedActivity(legacyValue), dataHost: null, readOnly: true, onChange: next => changes.push(next) });
    await settle();
    const reference = container.querySelector(".wf-data-reference")!;
    expect(reference.textContent).toContain("get-customer");
    expect(reference.querySelectorAll("button")).toHaveLength(0);
    expect(changes).toHaveLength(0);
  });
});

function afterEachUnmount() {
  if (!mounted) return;
  const current = mounted;
  flushSync(() => current.root.unmount());
  current.container.remove();
  mounted = null;
}
