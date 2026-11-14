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
import { activityDataMemberReferenceContribution, activityResultReferenceContribution } from "../activityResultReferenceContribution";
import type { ActivityDataSelectionHost, ActivityDataInputFinding } from "../activityDataSelection";
import type { ActivityDataContextResult, ActivityDataShape, ActivityDataSourceFacts, ActivityDataSourceQuery, ActivityDataTarget } from "../api/activityDataSelection";
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
    saving: false,
    requestSources: vi.fn(async (target: ActivityDataTarget, _signal: AbortSignal, _skip: number) => {
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
let setActivity: (next: ActivityNode) => void = () => undefined;
let setReadOnly: (next: boolean) => void = () => undefined;

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
    const [readOnly, updateReadOnly] = React.useState(options.readOnly ?? false);
    setReadOnly = updateReadOnly;
    setHost = setDataHost;
    setActivity = setCurrent;
    return (
      <ActivityPropertiesPanel
        activity={current}
        descriptor={descriptor}
        editors={[textEditor, moduleEditor]}
        expressionEditors={[activityResultReferenceContribution, activityDataMemberReferenceContribution]}
        expressionDescriptors={descriptors}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        readOnly={readOnly}
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
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    expect(dataHost.requests).toEqual([{ nodeId: "node-1", inputKey: "customerId" }]);
    // Only activity outputs are offered; the reported workflow input keeps its own editor.
    expect(chooser()?.textContent).not.toContain("Order ID");

    flushSync(() => option("Whole output", "Get customer")!.click());

    expect(changes).toHaveLength(1);
    const stored = changes[0]!.customerId as { expression: { type: string; value: unknown } };
    expect(stored.expression.type).toBe("ActivityResult");
    expect(stored.expression.value).toBe(customerBinding.value);
    expect(stored.expression.value).toEqual({ producerNodeId: "get-customer", projectionKey: "$result", isOptional: false });
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    await settle();
    expect(container.querySelector(".wf-data-reference-source")?.textContent).toBe("Source: Get customer › , Whole output");
    expect(document.activeElement).toBe(container.querySelector(".wf-data-reference-replace"));
    // Choosing data opens no fixed-value chrome and needs no save: only the source request ran.
    expect(dataHost.requestSources).toHaveBeenCalledTimes(1);
  });

  it("says a reason shared by all of a producer's outputs once, and keeps differing reasons on their rows", async () => {
    const loop = (projectionKey: string, reasonCode: string): ActivityDataSourceFacts => ({
      ...source(`activity:loop-step:${projectionKey}`, null, reasonCode, null),
      activityResultSource: { producerNodeId: "loop-step", projectionKey }
    });
    const mixed = (projectionKey: string, reasonCode: string): ActivityDataSourceFacts => ({
      ...source(`activity:get-order:${projectionKey}`, null, reasonCode, null),
      activityResultSource: { producerNodeId: "get-order", projectionKey }
    });
    const container = render({ dataHost: host({ requestSources: vi.fn(async () => answer("supported", { sources: [
      loop("Line", "EarlierProducerRun"), loop("Count", "EarlierProducerRun"),
      mixed("$result", "NoTypedResult"), mixed("Total", "ConditionalOccurrence")
    ] })) }) });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Line", "Loop step")).toBeDefined());

    const loopGroup = option("Line", "Loop step")!.closest("[role='group']")!;
    const shared = loopGroup.querySelector<HTMLElement>(".wf-data-chooser-group-reason")!;
    expect(shared.textContent).toContain("more than once");
    expect(loopGroup.querySelectorAll("[role='option'] small")).toHaveLength(0);
    for (const blocked of loopGroup.querySelectorAll("[role='option']")) {
      expect(blocked.getAttribute("aria-disabled")).toBe("true");
      expect(blocked.getAttribute("aria-describedby")).toBe(shared.id);
    }

    const orderGroup = option("Whole output", "Get order")!.closest("[role='group']")!;
    expect(orderGroup.querySelector(".wf-data-chooser-group-reason")).toBeNull();
    const rowReasons = [...orderGroup.querySelectorAll("[role='option']")].map(row =>
      document.getElementById(row.getAttribute("aria-describedby")!)?.textContent);
    expect(rowReasons).toHaveLength(2);
    expect(rowReasons[0]).toContain("doesn't produce a whole output");
    expect(rowReasons[1]).toContain("runs on only some paths");
  });

  it("shows sources that can't be chosen with their reason and never selects them", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ onChange: next => changes.push(next) });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Loop step")).toBeDefined());

    const blocked = option("Whole output", "Loop step")!;
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
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));

    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    expect(document.activeElement).toBe(option("Whole output", "Get order"));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true })));
    expect(document.activeElement).toBe(option("Whole output", "Loop step"));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));

    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(document.activeElement).toBe(trigger(container));
    expect(changes).toHaveLength(0);

    // Tab leaves the browser the same way, so focus never falls to the end of the document.
    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true })));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(document.activeElement).toBe(trigger(container));

    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(changes).toHaveLength(1);
    expect((changes[0]!.customerId as { expression: { value: unknown } }).expression.value).toBe(orderBinding.value);
  });

  it.each([
    ["denied", answer("denied", { code: "activity-data.denied" }), "don't have permission", false],
    ["policy-unavailable", answer("unavailable", { code: "activity-data.metadata-policy-unavailable" }), "restricts browsing outputs", false],
    ["analysis-unavailable", answer("unavailable", { code: "activity-data.analysis-unavailable" }), "aren't available right now", true],
    ["stale elsewhere", answer("stale", { code: "activity-data.base-changed" }), "changed somewhere else", false],
    ["incompatible", answer("incompatible", { code: "activity-data.response-unreadable" }), "update Studio", false],
    ["invalid target", answer("invalid", { code: "activity-data.invalid-source-or-target" }), "until its problems are fixed", true],
    ["empty", answer("empty"), "No earlier activity has an output for this input", false],
    ["only other roles", answer("supported", { sources: [sources[3]!] }), "No earlier activity has an output for this input", false],
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
    expect(chooser()?.querySelector("[aria-label='Loading outputs']")).not.toBeNull();

    release();
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("couldn't load the outputs"));
    const retry = [...chooser()!.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Retry")!;
    fail = false;
    flushSync(() => retry.click());
    expect(requestSources).toHaveBeenCalledTimes(2);
    release();
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
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
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("No earlier activity has an output for this input"));
  });

  it("keeps the list in place while a newer revision is asked about, storing a choice made meanwhile from the new answer", async () => {
    const changes: ActivityNode[] = [];
    const pending: Array<(result: ActivityDataContextResult) => void> = [];
    const requestSources = vi.fn(() => new Promise<ActivityDataContextResult>(resolve => pending.push(resolve)));
    const first = host({ requestSources });
    const container = render({ dataHost: first, onChange: next => changes.push(next) });
    await openChooser(container);
    pending[0]!(answer("supported", { sources }));
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));
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
    expect(option("Whole output", "Get order")?.getAttribute("aria-busy")).toBe("true");

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
    await vi.waitFor(() => expect(option("Whole output", "Get order")).toBeDefined());
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")?.getAttribute("aria-busy")).toBe("true"));
    flushSync(() => option("Whole output", "Get order")!.click());

    pending[1]!(answer("supported", { documentRevision: "rev-2", sources: [sources[0]!, { ...sources[1]!, binding: null, reasonCode: "ConditionalOccurrence" }] }));
    await vi.waitFor(() => expect(option("Whole output", "Get order")?.getAttribute("aria-disabled")).toBe("true"));
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
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("changed somewhere else"));

    flushSync(() => setHost({ ...first, baseRevision: "base-2" }));
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    expect(requestSources).toHaveBeenCalledTimes(2);
  });

  it("closes and forgets sources when the sign-in session ends", async () => {
    const container = render();
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
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
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    flushSync(() => option("Whole output", "Get customer")!.click());

    expect(changes).toHaveLength(0);
    const confirmation = container.querySelector<HTMLElement>("[role='alertdialog']")!;
    expect(confirmation.textContent).toContain("Activity output will replace the current value");
    const cancel = [...confirmation.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Cancel")!;
    flushSync(() => cancel.click());
    expect(changes).toHaveLength(0);

    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    flushSync(() => option("Whole output", "Get customer")!.click());
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
    await vi.waitFor(() => expect(option("Whole output", "Get customer")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));

    flushSync(() => option("Whole output", "Get order")!.click());
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

describe("Choose data after review round 1", () => {
  it("lists Activity output… in the syntax picker, which opens the chooser and changes nothing until an output is chosen", async () => {
    const changes: ActivityNode[] = [];
    const container = render({ onChange: next => changes.push(next) });
    const picker = container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    flushSync(() => picker.click());
    const entry = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")].find(item => item.textContent === "Activity output…")!;
    expect(entry.disabled).toBe(false);
    flushSync(() => entry.click());
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    expect(changes).toHaveLength(0);
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(changes).toHaveLength(0);
    expect(document.activeElement).toBe(picker);

    flushSync(() => picker.click());
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>("[role='option']")].find(item => item.textContent === "Activity output…")!.click());
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    flushSync(() => option("Whole output", "Get customer")!.click());
    expect(changes).toHaveLength(1);
    expect((changes[0]!.customerId as { expression: { type: string } }).expression.type).toBe("ActivityResult");
  });

  it("uses one icon for the action, the stored reference and its mode", async () => {
    const container = render();
    expect(trigger(container)?.querySelector("svg.lucide-corner-down-right")).not.toBeNull();
    afterEachUnmount();
    const stored = render({ activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "ActivityResult", value: customerBinding.value } } } });
    await settle();
    expect(stored.querySelector(".wf-data-reference-source svg.lucide-corner-down-right")).not.toBeNull();
    expect(stored.querySelector(".wf-syntax-picker-trigger svg.lucide-corner-down-right")).not.toBeNull();
    expect(stored.querySelector(".wf-syntax-picker-trigger svg.lucide-cable")).toBeNull();
  });

  it("tells a stored reference to one of two same-named activities apart by its identity", async () => {
    const container = render({
      activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
        customerId: { typeName: "System.String", expression: { type: "ActivityResult", value: customerBinding.value } } },
      dataHost: host({ describeActivity: nodeId => labels[nodeId] ? { label: "Timer", outputLabels: {}, sharesLabel: true } : null })
    });
    await settle();
    expect(container.querySelector(".wf-data-reference-source")?.textContent).toBe("Source: Timer · get-customer › , Whole output");
  });

  it("marks an output that can't be chosen without relying on colour, in its accessible name", async () => {
    const container = render();
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Loop step")).toBeDefined());
    const blocked = option("Whole output", "Loop step")!;
    expect(blocked.querySelector(".wf-data-chooser-option-tag")?.textContent).toBe("Not available");
    expect(blocked.textContent).toContain(", Not available");
    expect(option("Whole output", "Get customer")!.textContent).toBe("Whole output, Customer");
    // Group headings and shared reasons name and describe the options; they are not listbox content of their own.
    for (const label of document.querySelectorAll(".wf-data-chooser-group-label")) expect(label.getAttribute("aria-hidden")).toBe("true");
  });

  it("waits out a stale answer while the editor's own save is in flight", async () => {
    const requestSources = vi.fn()
      .mockResolvedValueOnce(answer("stale", { code: "activity-data.base-changed" }))
      .mockResolvedValueOnce(answer("supported", { sources }));
    const first = host({ requestSources, saving: true });
    const container = render({ dataHost: first });
    await openChooser(container);
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("Updating for your latest changes"));
    expect(chooser()?.textContent).not.toContain("changed somewhere else");
    expect(requestSources).toHaveBeenCalledTimes(1);
    flushSync(() => setHost({ ...first, saving: false, baseRevision: "base-2" }));
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
  });

  it("offers a reload when the workflow changed somewhere else", async () => {
    const container = render({ dataHost: host({ requestSources: vi.fn(async () => answer("stale", { code: "activity-data.base-changed" })) }) });
    await openChooser(container);
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("changed somewhere else"));
    expect([...chooser()!.querySelectorAll("button")].map(button => button.textContent)).toEqual(["Reload this workflow"]);
  });

  it("says so when a choice made while updating can no longer be used", async () => {
    const changes: ActivityNode[] = [];
    const pending: Array<(result: ActivityDataContextResult) => void> = [];
    const first = host({ requestSources: vi.fn(() => new Promise<ActivityDataContextResult>(resolve => pending.push(resolve))) });
    const container = render({ dataHost: first, onChange: next => changes.push(next) });
    await openChooser(container);
    pending[0]!(answer("supported", { sources }));
    await vi.waitFor(() => expect(option("Whole output", "Get order")).toBeDefined());
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")?.getAttribute("aria-busy")).toBe("true"));
    flushSync(() => option("Whole output", "Get order")!.click());
    pending[1]!(answer("supported", { documentRevision: "rev-2", sources: [sources[0]!, { ...sources[1]!, binding: null, reasonCode: "ConditionalOccurrence" }] }));
    await vi.waitFor(() => expect(chooser()?.querySelector(".wf-data-chooser-lost")?.textContent).toContain("can no longer be used"));
    expect(changes).toHaveLength(0);
  });

  it("reads outputs listed after the first page and says where it stopped", async () => {
    const inputsOnly = Array.from({ length: 100 }, (_, index) => ({ ...sources[3]!, id: `input:${index}` }));
    const pages: number[] = [];
    const container = render({ dataHost: host({ requestSources: vi.fn(async (_target: ActivityDataTarget, _signal: AbortSignal, skip: number) => {
      pages.push(skip);
      return skip === 0 ? answer("supported", { sources: inputsOnly, hasMoreSources: true }) : answer("supported", { sources: [sources[0]!] });
    }) }) });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    expect(pages).toEqual([0, 100]);
    expect(chooser()?.textContent).not.toContain("first 1,000");
    afterEachUnmount();

    const endless = render({ dataHost: host({ requestSources: vi.fn(async (_target: ActivityDataTarget, _signal: AbortSignal, skip: number) =>
      answer("supported", { sources: skip === 0 ? [sources[0]!, ...inputsOnly.slice(1)] : inputsOnly, hasMoreSources: true })) }) });
    await openChooser(endless);
    await vi.waitFor(() => expect(chooser()?.textContent).toContain("Showing the activity outputs among the first 1,000"));
    expect(option("Whole output", "Get customer")).toBeDefined();
  });

  it("never stores a pending choice in another activity shown in the same row", async () => {
    const changes: ActivityNode[] = [];
    let resolve!: (result: ActivityDataContextResult) => void;
    const container = render({
      onChange: next => changes.push(next),
      dataHost: host({ requestSources: vi.fn(() => new Promise<ActivityDataContextResult>(done => { resolve = done; })) })
    });
    await openChooser(container);
    flushSync(() => setActivity({ nodeId: "node-2", activityVersionId: "invoice-1", inputs: [], outputs: [] }));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    resolve(answer("supported", { sources }));
    await settle();
    expect(changes).toHaveLength(0);
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
  });

  it("does not offer Choose data on an input typed as a connection reference", () => {
    const container = render({ inputs: [input("Connection", { typeName: "Elsa.Activities.Runtime.Core.Models.ConnectionReference, Elsa.Activities.Runtime.Core" })] });
    expect(trigger(container)).toBeNull();
  });

  it("closes on Tab and keeps focus in the row", async () => {
    const container = render();
    await openChooser(container);
    await vi.waitFor(() => expect(document.activeElement).toBe(option("Whole output", "Get customer")));
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true })));
    expect(document.querySelector(".wf-data-chooser")).toBeNull();
    expect(container.contains(document.activeElement)).toBe(true);
  });
});

describe("Choose data after review round 2", () => {
  const lostNote = () => document.querySelector(".wf-data-chooser-lost");

  // Opens the chooser on a first listing, starts a refresh for rev-2 and chooses Get order while it runs.
  async function chooseWhileRefreshing(changes: ActivityNode[], hostOverrides: Partial<ActivityDataSelectionHost> = {}) {
    const pending: Array<(result: ActivityDataContextResult) => void> = [];
    const failures: Array<(error: Error) => void> = [];
    const first = host({ requestSources: vi.fn(() => new Promise<ActivityDataContextResult>((resolve, reject) => {
      pending.push(resolve);
      failures.push(reject);
    })), ...hostOverrides });
    const container = render({ dataHost: first, onChange: next => changes.push(next) });
    await openChooser(container);
    pending[0]!(answer("supported", { sources }));
    await vi.waitFor(() => expect(option("Whole output", "Get order")).toBeDefined());
    flushSync(() => setHost({ ...first, documentRevision: "rev-2" }));
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")?.getAttribute("aria-busy")).toBe("true"));
    flushSync(() => option("Whole output", "Get order")!.click());
    return { pending, failures, first };
  }

  it("names the producer when a choice made while updating can no longer be used, and clears that on the next listing", async () => {
    const changes: ActivityNode[] = [];
    const { pending, first } = await chooseWhileRefreshing(changes);
    pending[1]!(answer("supported", { documentRevision: "rev-2", sources: [sources[0]!, { ...sources[1]!, binding: null, reasonCode: "ConditionalOccurrence" }] }));
    await vi.waitFor(() => expect(lostNote()?.textContent).toBe(
      "Get order › Whole output can no longer be used for this input after your latest changes. Choose another output."));
    expect(changes).toHaveLength(0);

    flushSync(() => setHost({ ...first, documentRevision: "rev-3" }));
    pending[2]!(answer("supported", { documentRevision: "rev-3", sources }));
    await vi.waitFor(() => expect(lostNote()).toBeNull());
    expect(changes).toHaveLength(0);
  });

  it.each([
    ["fails", null],
    ["ends stale while saving", answer("stale", { documentRevision: "rev-2", code: "activity-data.base-changed" })],
    ["is refused", answer("denied", { documentRevision: "rev-2", code: "activity-data.denied" })]
  ])("drops a pending choice silently when the refresh %s", async (_name, outcome) => {
    const changes: ActivityNode[] = [];
    const { pending, failures } = await chooseWhileRefreshing(changes, { saving: true });
    if (outcome) pending[1]!(outcome);
    else failures[1]!(new Error("offline"));
    await vi.waitFor(() => expect(chooser()?.querySelector("[role='listbox']")).toBeNull());
    expect(lostNote()).toBeNull();
    expect(changes).toHaveLength(0);
  });

  it("never stores from a list shown for an older revision than the one on screen", async () => {
    const changes: ActivityNode[] = [];
    // The list answers rev-1 while the document on screen is already rev-2 and its refresh has not answered yet.
    const container = render({
      onChange: next => changes.push(next),
      dataHost: host({ documentRevision: "rev-2", requestSources: vi.fn(async () => answer("supported", { documentRevision: "rev-1", sources })) })
    });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Get customer")).toBeDefined());
    flushSync(() => option("Whole output", "Get customer")!.click());
    await settle();
    expect(changes).toHaveLength(0);
  });

  it("advises a variable only for declared outputs of a looping producer", async () => {
    const looped = (projectionKey: string): ActivityDataSourceFacts => ({
      ...source(`activity:loop-step:${projectionKey}`, null, "EarlierProducerRun", null),
      activityResultSource: { producerNodeId: "loop-step", projectionKey }
    });
    const container = render({ dataHost: host({ requestSources: vi.fn(async () => answer("supported", { sources: [looped("$result"), looped("Line")] })) }) });
    await openChooser(container);
    await vi.waitFor(() => expect(option("Whole output", "Loop step")).toBeDefined());
    const reasonOf = (row: Element) => document.getElementById(row.getAttribute("aria-describedby")!)?.textContent ?? "";
    expect(reasonOf(option("Whole output", "Loop step")!)).toContain("Use an expression for this input instead.");
    expect(reasonOf(option("Whole output", "Loop step")!)).not.toContain("Outputs tab");
    expect(reasonOf(option("Line", "Loop step")!)).toContain("Outputs tab");
  });

  it("includes the node id in Go to when the activity's name is shared", async () => {
    const container = render({
      activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
        customerId: { typeName: "System.String", expression: { type: "ActivityResult", value: customerBinding.value } } },
      dataHost: host({ describeActivity: nodeId => labels[nodeId] ? { label: "Timer", outputLabels: {}, sharesLabel: true } : null })
    });
    await settle();
    expect(container.querySelector(".wf-data-reference-actions button")?.getAttribute("aria-label")).toBe("Go to Timer · get-customer");
  });
});


function memberShape(overrides: Partial<ActivityDataShape> = {}): ActivityDataShape {
  return { state: "known", kind: "object", type: { alias: "Customer", collectionKind: "Single" }, members: [],
    sourceContractRevision: "c", profileRevision: "p", metadataRevision: "m", isNullable: false, presence: "required",
    representation: "StructuredValue", isSensitive: false, isCredential: false, reasonCode: null, element: null, mapValue: null, ...overrides };
}
function nestedHost(overrides: Partial<ActivityDataSelectionHost> = {}) {
  const leaf = memberShape({ kind: "scalar", type: { alias: "String", collectionKind: "Single" } });
  const root = { ...sources[0]!, shape: memberShape({ members: [{ serializedName: "customerId", displayName: "Customer ID", shape: leaf }] }) };
  const memberBinding = { expressionType: "ActivityResultMember", value: { ...customerBinding.value, memberPath: ["customerId"] } };
  const answerFor = (query?: ActivityDataSourceQuery) => answer("supported", { memberReadsEnabled: true,
    target: { displayName: "Customer ID", constraint: null, type: { alias: "String", collectionKind: "Single" } },
    sources: query?.memberPath?.length ? [{ ...root, binding: memberBinding, shape: leaf }] : [root] });
  return { root, memberBinding, answerFor, dataHost: host({ requestSources: vi.fn(async (_target, _signal, _skip, query) => answerFor(query)), ...overrides }) };
}
function dataOption(text: string) {
  return [...document.querySelectorAll<HTMLButtonElement>(".wf-data-members [role='option']")].find(option => option.textContent?.includes(text));
}

describe("Choose data with member capability", () => {
  it("browses Customer and stores the server's customerId member binding as one edit, then clears it", async () => {
    const changes = vi.fn();
    const { dataHost, memberBinding } = nestedHost();
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    expect([...document.querySelectorAll("[role='option']")].every(option => !option.querySelector("button,input,select"))).toBe(true);
    expect(document.querySelector(".wf-data-member-use")?.textContent).toBe("Use whole value");
    expect(document.querySelector(".wf-data-members")?.textContent).toContain("Input type: String");
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(changes).toHaveBeenCalledTimes(1));
    expect(changes.mock.calls[0]![0].customerId.expression).toEqual({ type: memberBinding.expressionType, value: memberBinding.value });
    await vi.waitFor(() => expect(container.querySelector(".wf-data-reference-source")?.textContent).toContain("customerId"));
    expect(container.querySelector(".wf-data-reference-source")?.textContent).toContain("Get customer");
    const clear = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.getAttribute("aria-label") === "Clear data for Customer ID")!;
    flushSync(() => clear.click());
    expect(changes).toHaveBeenCalledTimes(2);
    expect(changes.mock.calls[1]![0]).not.toHaveProperty("customerId");
  });

  it.each([false, true])("aborts a focused choice on same-key/revision target switch (reference bridge:%s)", async viaReference => {
    const changes = vi.fn();
    const original = nestedHost();
    let finish: (() => void) | undefined;
    let signal: AbortSignal | undefined;
    const dataHost = host({ requestSources: vi.fn(async (_target, requestSignal, _skip, query) => {
      if (query?.memberPath?.length) return new Promise<ActivityDataContextResult>(resolve => { signal = requestSignal; finish = () => resolve(original.answerFor(query)); });
      return original.answerFor(query);
    }) });
    const activity = viaReference ? { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "ActivityResult", value: customerBinding.value } } } : undefined;
    const container = render({ dataHost, activity, onChange: changes });
    if (viaReference) { await settle(); flushSync(() => container.querySelector<HTMLButtonElement>(".wf-data-reference-replace")!.click()); }
    else await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(finish).toBeDefined());
    flushSync(() => setActivity({ nodeId: "node-2", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "Literal", value: "preserved" } } }));
    expect(signal?.aborted).toBe(true);
    finish!();
    await settle();
    expect(changes).not.toHaveBeenCalled();
  });
  it("does not commit or queue a confirmation after the input becomes read-only", async () => {
    const changes = vi.fn();
    const original = nestedHost();
    let finish: (() => void) | undefined;
    let signal: AbortSignal | undefined;
    const dataHost = host({ requestSources: vi.fn(async (_target, requestSignal, _skip, query) => {
      if (query?.memberPath?.length) return new Promise<ActivityDataContextResult>(resolve => { signal = requestSignal; finish = () => resolve(original.answerFor(query)); });
      return original.answerFor(query);
    }) });
    const container = render({ dataHost, activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "Literal", value: "preserved" } } }, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(finish).toBeDefined());
    flushSync(() => setReadOnly(true));
    expect(signal?.aborted).toBe(true);
    finish!(); await settle();
    expect(changes).not.toHaveBeenCalled();
    expect(document.querySelector(".wf-expression-transition-confirmation")).toBeNull();
  });

  it.each(["WorkflowRequest", "WorkflowRequestMember"])("preserves an undeclared %s until explicit clear", async kind => {
    const changes = vi.fn();
    const value = kind === "WorkflowRequest"
      ? { memberKey: "OldPayload", path: null, futureField: { preserved: true } }
      : { memberKey: "OldPayload", memberPath: ["a.b", ""], futureField: { preserved: true } };
    const dataHost = host({ findingsFor: () => [], requestSources: vi.fn(async () => answer("invalid", {
      findings: [{ path: "node-1/inputs/customerId", type: "ActivityData/UnknownInput", message: "Declare the input." }]
    })) });
    const activity = { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: kind, value } } };
    const before = JSON.stringify(activity);
    const container = render({ dataHost, activity, onChange: changes });
    await vi.waitFor(() => expect(container.querySelector(".wf-data-reference-problem")?.textContent).toContain("Declare it"));
    expect(JSON.stringify(activity)).toBe(before);
    expect(changes).not.toHaveBeenCalled();
    const replace = container.querySelector<HTMLButtonElement>("button[aria-label='Replace data for Customer ID']")!;
    expect(replace.disabled).toBe(false);
    flushSync(() => replace.click());
    await vi.waitFor(() => expect(document.querySelector(".wf-data-chooser")).not.toBeNull());
    flushSync(() => document.querySelector<HTMLElement>(".wf-data-chooser")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(changes).not.toHaveBeenCalled();
    expect(JSON.stringify(activity)).toBe(before);
    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Clear data for Customer ID']")!.click());
    expect(changes).toHaveBeenCalledTimes(1);
    expect(changes.mock.calls[0]![0]).not.toHaveProperty("customerId");
  });

  it("does not offer an undeclared request-name entry, including an obsolete host flag", async () => {
    const changes = vi.fn();
    const dataHost = Object.assign(host({ requestSources: vi.fn(async () => answer("supported", {
      memberReadsEnabled: true, target: { displayName: "Customer ID", constraint: null }, sources: [{ ...sources[0]!, shape: memberShape() }]
    })) }), { openWorkflowRequest: true });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members")).not.toBeNull());
    expect(document.querySelector("input[aria-label='Request member']")).toBeNull();
    expect(document.body.textContent).not.toContain("Open request member");
    expect(changes).not.toHaveBeenCalled();
  });

  it.each(["variable", "input"])("selects exact nested and empty keys from a %s using only server offers", async family => {
    const changes = vi.fn();
    const rootBinding = family === "variable" ? { expressionType: "Variable", value: { referenceKey: "stable-key", declaringScopeId: "scope" } }
      : { expressionType: "WorkflowRequest", value: { memberKey: "Payload", path: null } };
    const id = family === "variable" ? "variable:scope:stable-key" : "input:stable-key";
    const rootSource = { id, displayName: "Payload", binding: rootBinding, reasonCode: null, availability: "definite", activityResultSource: null,
      type: null, shape: memberShape({ state: "unknown", kind: "unknown", type: null }) };
    let commitOffer: NonNullable<ActivityDataSourceFacts["binding"]> | null = null;
    let choosing = false;
    const dataHost = host({ requestSources: vi.fn(async (_target, _signal, _skip, query) => {
      const path = [...query?.memberPath ?? []];
      const binding = path.length ? { expressionType: family === "variable" ? "VariableMember" : "WorkflowRequestMember",
        value: { ...(family === "variable" ? rootBinding.value : { memberKey: "Payload" }), memberPath: path } } : rootBinding;
      if (choosing && !commitOffer) commitOffer = binding;
      return answer("supported", { memberReadsEnabled: true, target: { displayName: "Customer ID", constraint: null },
        sources: [{ ...rootSource, binding }] });
    }) });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Payload")).toBeDefined());
    flushSync(() => dataOption("Payload")!.click());
    await vi.waitFor(() => expect(document.querySelector("input[aria-label='Member name']")).not.toBeNull());
    enterMemberText("Member name", "a.b");
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Open member")!.click());
    await vi.waitFor(() => expect(document.querySelector("[aria-label='Member path']")?.textContent).toContain('"a.b"'));
    await vi.waitFor(() => expect(document.querySelector<HTMLButtonElement>(".wf-data-member-use")?.disabled).toBe(false));
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Open empty member")!.click());
    await vi.waitFor(() => expect(document.querySelector("[aria-label='Member path']")?.textContent).toContain('""'));
    await vi.waitFor(() => expect(document.querySelector<HTMLButtonElement>(".wf-data-member-use")?.disabled).toBe(false));
    choosing = true;
    flushSync(() => document.querySelector<HTMLButtonElement>(".wf-data-member-use")!.click());
    await vi.waitFor(() => expect(changes).toHaveBeenCalledTimes(1));
    expect(changes.mock.calls[0]![0].customerId.expression).toMatchObject({ type: family === "variable" ? "VariableMember" : "WorkflowRequestMember",
      value: { memberPath: ["a.b", ""] } });
    expect(changes.mock.calls[0]![0].customerId.expression.value).toBe(commitOffer!.value);
  });

  it.each(["ValueFlow/UnavailableProducer", "ActivityData/UnknownMember", "ActivityData/UnknownInput"])("shows current unsaved reference refusal %s without saved findings or an edit", async type => {
    const changes = vi.fn();
    const { memberBinding } = nestedHost();
    const dataHost = host({ documentRevision: "unsaved-2", findingsFor: () => [], requestSources: vi.fn(async () => answer("invalid", {
      documentRevision: "unsaved-2", memberReadsEnabled: true, target: { displayName: "LEAK", type: { alias: "LEAK", collectionKind: "Single" }, constraint: null },
      sources: [{ ...sources[0]!, displayName: "LEAK", binding: memberBinding, shape: memberShape({ type: { alias: "LEAK", collectionKind: "Single" } }) }],
      findings: [{ path: "node-1/inputs/customerId", type, message: "Raw server text is not presentation authority." }]
    })) });
    const container = render({ dataHost, onChange: changes, activity: {
      nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: memberBinding.expressionType, value: memberBinding.value } }
    } });
    await vi.waitFor(() => expect(container.querySelector(".wf-data-reference-problem")?.textContent).toContain(type === "ActivityData/UnknownInput" ? "Declare it" : "Replace it or clear it"));
    expect(container.querySelector(".wf-data-reference")?.getAttribute("data-problem")).toBe("true");
    expect(container.textContent).not.toContain("LEAK");
    expect(container.textContent).not.toContain("Raw server text");
    expect(changes).not.toHaveBeenCalled();
    expect(container.querySelector<HTMLButtonElement>("button[aria-label='Replace data for Customer ID']")?.disabled).toBe(false);
    const clear = container.querySelector<HTMLButtonElement>("button[aria-label='Clear data for Customer ID']")!;
    expect(clear.disabled).toBe(false);
    flushSync(() => clear.click());
    expect(changes).toHaveBeenCalledTimes(1);
    expect(changes.mock.calls[0]![0]).not.toHaveProperty("customerId");
  });

  it("does not present a refused finding belonging to another target or an older document", async () => {
    const { memberBinding } = nestedHost();
    const first = host({ requestSources: vi.fn(async () => answer("invalid", { findings: [
      { path: "other/inputs/customerId", type: "ValueFlow/UnavailableProducer", message: "Other target" }
    ] })) });
    const container = render({ dataHost: first, activity: { nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: memberBinding.expressionType, value: memberBinding.value } } } });
    await settle();
    expect(container.querySelector(".wf-data-reference-problem")).toBeNull();
    let finish: (() => void) | undefined;
    let signal: AbortSignal | undefined;
    flushSync(() => setHost({ ...first, documentRevision: "rev-2", requestSources: async (_target, requestSignal) => new Promise<ActivityDataContextResult>(resolve => {
      signal = requestSignal; finish = () => resolve(answer("invalid", { documentRevision: "rev-2", findings: [
        { path: "node-1/inputs/customerId", type: "ActivityData/UnknownMember", message: "Old document" }
      ] }));
    }) }));
    await vi.waitFor(() => expect(finish).toBeDefined());
    flushSync(() => setHost({ ...first, documentRevision: "rev-3" }));
    expect(signal?.aborted).toBe(true);
    finish!(); await settle();
    expect(container.querySelector(".wf-data-reference-problem")).toBeNull();
  });

  it("keeps an out-of-scope stored member intact without replacement or clear actions", async () => {
    const changes = vi.fn();
    const { memberBinding } = nestedHost();
    const stored = { ...memberBinding.value, futureField: { retained: true } };
    const container = render({ dataHost: null, onChange: changes, activity: {
      nodeId: "node-1", activityVersionId: "invoice-1", inputs: [], outputs: [],
      customerId: { typeName: "System.String", expression: { type: "ActivityResultMember", value: stored } }
    } });
    await settle();
    expect(container.textContent).toContain("The reference is preserved");
    expect([...container.querySelectorAll("button")].some(button => /^(Replace|Clear)$/.test(button.textContent ?? ""))).toBe(false);
    expect(changes).not.toHaveBeenCalled();
    expect(stored.futureField).toEqual({ retained: true });
  });

  it("supports keyboard member selection, internal Tab and Escape without committing a browsed object", async () => {
    const changes = vi.fn();
    const container = render({ dataHost: nestedHost().dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => { dataOption("Get customer")!.focus(); dataOption("Get customer")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })); });
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    await settle();
    flushSync(() => dataOption("customerId")!.focus());
    flushSync(() => dataOption("customerId")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true })));
    expect(document.activeElement).toBe(document.querySelector(".wf-data-member-use"));
    expect(changes).not.toHaveBeenCalled();
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members")).toBeNull());
    expect(document.activeElement).toBe(trigger(container));
    expect(changes).not.toHaveBeenCalled();
  });

  it.each(["whole fallback", "wrong path", "legacy path"])("rejects a malformed focused %s offer without changing authored data", async malformed => {
    const changes = vi.fn();
    const original = nestedHost();
    const dataHost = host({ requestSources: vi.fn(async (_target, _signal, _skip, query) => {
      const result = original.answerFor(query);
      if (!query?.memberPath?.length) return result;
      const binding = malformed === "whole fallback" ? customerBinding : { ...original.memberBinding,
        value: { ...original.memberBinding.value, ...(malformed === "wrong path" ? { memberPath: ["wrong"] } : { path: null }) } };
      return { ...result, sources: [{ ...result.sources[0]!, binding }] };
    }) });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(changes.mock.calls.length > 0 || document.querySelector(".wf-data-members [role='alert'],.wf-data-members [role='status']") != null).toBe(true));
    expect(changes).not.toHaveBeenCalled();
    expect(document.querySelector<HTMLButtonElement>(".wf-data-member-use")?.disabled).toBe(true);
  });

  it("drops a late focused choice after Back and Escape, restoring focus without an edit", async () => {
    const changes = vi.fn();
    const original = nestedHost();
    let finish: (() => void) | undefined;
    let signal: AbortSignal | undefined;
    const dataHost = host({ requestSources: vi.fn(async (_target, requestSignal, _skip, query) => {
      if (query?.memberPath?.length) return new Promise<ActivityDataContextResult>(resolve => {
        signal = requestSignal; finish = () => resolve(original.answerFor(query));
      });
      return original.answerFor(query);
    }) });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(finish).toBeDefined());
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>(".wf-data-members button")].find(button => button.textContent === "Back")!.click());
    expect(signal?.aborted).toBe(true);
    finish!(); await settle();
    expect(changes).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(document.activeElement).toBe(dataOption("customerId")));
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>(".wf-data-members button")].find(button => button.textContent === "Back")!.click());
    await vi.waitFor(() => expect(document.activeElement).toBe(dataOption("Get customer")));
    flushSync(() => document.querySelector(".wf-data-members")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members")).toBeNull());
    expect(document.activeElement?.getAttribute("aria-label")).toContain("Choose data");
  });

  it("retries a focused failure and falls back to whole-output browsing when capability is withdrawn", async () => {
    const changes = vi.fn();
    const original = nestedHost();
    let fail = true;
    let enabled = true;
    const dataHost = host({ requestSources: vi.fn(async (_target, _signal, _skip, query) => {
      if (query?.source && fail) { fail = false; throw new Error("temporary network failure"); }
      if (query?.memberPath?.length) enabled = false;
      return enabled ? original.answerFor(query) : answer("supported", { memberReadsEnabled: false, sources: [original.root] });
    }) });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members [role='alert']")?.textContent).toContain("couldn't load"));
    expect(changes).not.toHaveBeenCalled();
    flushSync(() => [...document.querySelectorAll<HTMLButtonElement>(".wf-data-members button")].find(button => button.textContent === "Retry")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members")).toBeNull());
    expect(option("Whole output", "Get customer")).toBeDefined();
    expect(changes).not.toHaveBeenCalled();
  });

  it("purges cached sources when a focused request loses access", async () => {
    const changes = vi.fn();
    const original = nestedHost();
    const dataHost = host({ requestSources: vi.fn(async (_target, _signal, _skip, query) => query?.memberPath?.length
      ? answer("denied") : original.answerFor(query)) });
    const container = render({ dataHost, onChange: changes });
    await openChooser(container);
    await vi.waitFor(() => expect(dataOption("Get customer")).toBeDefined());
    flushSync(() => dataOption("Get customer")!.click());
    await vi.waitFor(() => expect(dataOption("customerId")).toBeDefined());
    flushSync(() => dataOption("customerId")!.click());
    await vi.waitFor(() => expect(document.querySelector(".wf-data-members")).toBeNull());
    expect(document.querySelector("[role='option']")).toBeNull();
    expect(document.querySelector(".wf-data-chooser")?.textContent).toContain("permission");
    expect(changes).not.toHaveBeenCalled();
  });

});

function enterMemberText(label: string, value: string) {
  const input = document.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
