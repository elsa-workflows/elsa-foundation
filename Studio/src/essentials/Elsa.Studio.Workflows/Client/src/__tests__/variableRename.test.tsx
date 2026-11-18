import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { VariablesEditor } from "../WorkflowPropertiesView";
import { VariableRenameContext } from "../variableRenameContext";
import { applyVariableRenameEdits, withVariableName, workflowVariableScopeId } from "../variableRename";
import { VariableRenameNotice, useVariableRename } from "../workflow-editor/VariableRenameNotice";
import { clearApiCapabilityCache } from "../api/capabilities";
import { setNotifications } from "../workflow-editor/notifications";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
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

async function waitFor(assertion: () => void) {
  for (let attempt = 0; ; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      if (attempt > 50) throw error;
      await new Promise(resolve => setTimeout(resolve, 5));
    }
  }
}

const order = { referenceKey: "order-key", name: "order", type: { alias: "Object", collectionKind: "Single" as const }, storageDriverType: null, default: null };

function writeLine(nodeId: string, source: string): ActivityNode {
  return { nodeId, activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null, Text: { typeName: "String", expression: { type: "JavaScript", value: source } } };
}

function state(root: ActivityNode, variables = [order]): WorkflowDefinitionState {
  return { variables, rootActivity: root, inputs: [], outputs: [] };
}

function container(variables: unknown[], ...children: ActivityNode[]): ActivityNode {
  return { nodeId: "box", activityVersionId: "sequence-v1", inputs: [], outputs: [], structure: { kind: "Sequence", schemaVersion: "1", payload: { activities: children, variables } } };
}

describe("variable rename edits", () => {
  it("restores the old name on the declaring scope only", () => {
    const inner = { ...order, referenceKey: "inner-key" };
    const draft = state(container([inner]));

    expect(withVariableName(draft, workflowVariableScopeId, "order-key", "before").variables?.[0].name).toBe("before");
    const scoped = withVariableName(draft, "box", "inner-key", "before");
    expect((scoped.rootActivity!.structure!.payload.variables as { name: string }[])[0].name).toBe("before");
    expect(scoped.variables?.[0].name).toBe("order");
  });

  it("restores the old name of a variable keyed with the legacy ReferenceKey spelling", () => {
    const legacy = { ReferenceKey: "order-key", name: "purchase", type: order.type } as never;

    expect(withVariableName(state(writeLine("a", "x"), [legacy]), workflowVariableScopeId, "order-key", "order").variables?.[0].name).toBe("order");
  });

  it("edits authored inputs and wire entries whose source still matches, and skips the rest", () => {
    const wire: ActivityNode = {
      nodeId: "wire", activityVersionId: "write-line-v1", outputs: [], structure: null,
      inputs: [{ referenceKey: "Text", value: { value: { language: "JavaScript", source: "variables.order" }, expressionType: "JavaScript" } }]
    };
    const draft = state(writeLine("a", "variables.order.total"));

    const authored = applyVariableRenameEdits(draft, [
      { nodeId: "a", inputKey: "Text", oldSource: "variables.order.total", newSource: "variables.purchase.total" },
      { nodeId: "a", inputKey: "Text", oldSource: "something else", newSource: "x" }
    ]);
    const fromWire = applyVariableRenameEdits(state(wire), [{ nodeId: "wire", inputKey: "Text", oldSource: "variables.order", newSource: "variables.purchase" }]);

    expect((authored.state.rootActivity!.Text as { expression: { value: string } }).expression.value).toBe("variables.purchase.total");
    expect(authored.applied).toHaveLength(1);
    expect(authored.skipped).toHaveLength(1);
    expect((fromWire.state.rootActivity!.inputs[0] as { value: { value: { source: string } } }).value.value.source).toBe("variables.purchase");
  });
});

describe("variable editor rename reporting", () => {
  function editor(onVariableRenamed: ReturnType<typeof vi.fn>, onChange = vi.fn()) {
    return (items: Record<string, unknown>[]) => (
      <VariableRenameContext.Provider value={{ scopeId: "box", onVariableRenamed }}>
        <VariablesEditor items={items} typeOptions={[]} storageOptions={null} editorForAlias={() => "text"} onChange={onChange} />
      </VariableRenameContext.Provider>
    );
  }

  it("reports a committed rename once, with the name it had when editing began", () => {
    const onVariableRenamed = vi.fn();
    const view = editor(onVariableRenamed);
    const host = render(view([order]));
    const input = host.querySelector<HTMLInputElement>("input[aria-label='Variable name']")!;

    flushSync(() => input.focus());
    flushSync(() => active!.root.render(view([{ ...order, name: "ord" }])));
    flushSync(() => active!.root.render(view([{ ...order, name: "purchase" }])));
    expect(onVariableRenamed).not.toHaveBeenCalled();
    flushSync(() => input.blur());

    expect(onVariableRenamed).toHaveBeenCalledTimes(1);
    expect(onVariableRenamed).toHaveBeenCalledWith("box", "order-key", "order", "purchase");
  });

  it("reports a rename still being typed when the editor unmounts", () => {
    const onVariableRenamed = vi.fn();
    const view = editor(onVariableRenamed);
    const host = render(view([order]));
    const input = host.querySelector<HTMLInputElement>("input[aria-label='Variable name']")!;

    flushSync(() => input.focus());
    flushSync(() => active!.root.render(view([{ ...order, name: "purchase" }])));
    flushSync(() => active!.root.render(<p>Another view</p>));

    expect(onVariableRenamed).toHaveBeenCalledTimes(1);
    expect(onVariableRenamed).toHaveBeenCalledWith("box", "order-key", "order", "purchase");
  });

  it("reports nothing when the name is unchanged or emptied", () => {
    const onVariableRenamed = vi.fn();
    const view = editor(onVariableRenamed);
    const host = render(view([order]));
    const input = host.querySelector<HTMLInputElement>("input[aria-label='Variable name']")!;

    flushSync(() => input.focus());
    flushSync(() => input.blur());
    flushSync(() => input.focus());
    flushSync(() => active!.root.render(view([{ ...order, name: "  " }])));
    flushSync(() => input.blur());

    expect(onVariableRenamed).not.toHaveBeenCalled();
  });
});

describe("rename follow-up", () => {
  function api(plan: object | null, links = true) {
    const getJson = vi.fn().mockResolvedValue({
      capabilities: [{
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: links ? [{ rel: "scoped-variable-rename-plan", href: "design/workflows/scoped-variables/rename-plan" }] : []
      }]
    });
    const postJson = plan instanceof Error ? vi.fn().mockRejectedValue(plan) : vi.fn().mockResolvedValue(plan);
    return { context: { baseUrl: `test://rename-${Math.random()}`, http: { getJson, postJson } } as never, postJson };
  }

  function Harness({ context, draft, updateState, onSelectReference = vi.fn() }: {
    context: never;
    draft: WorkflowDefinitionState;
    updateState: ReturnType<typeof vi.fn>;
    onSelectReference?: ReturnType<typeof vi.fn>;
  }) {
    const rename = useVariableRename({ context, state: draft, updateState });
    return (
      <>
        <button type="button" onClick={() => rename.onVariableRenamed(workflowVariableScopeId, "order-key", "order", "purchase")}>rename</button>
        <button type="button" onClick={() => rename.onVariableRenamed(workflowVariableScopeId, "order-key", "purchase", "basket")}>rename again</button>
        <VariableRenameNotice
          outcome={rename.outcome}
          labelFor={(nodeId, inputKey) => ({ activity: `Node ${nodeId}`, input: `Input ${inputKey}` })}
          onSelectReference={onSelectReference}
          onDismiss={rename.dismiss}
        />
      </>
    );
  }

  it("plans from the old name, applies the edits and lists what to repair by hand", async () => {
    const renamed = state(writeLine("a", "variables.order.total"), [{ ...order, name: "purchase" }]);
    const { context, postJson } = api({
      oldName: "order",
      newName: "purchase",
      edits: [{ nodeId: "a", inputKey: "Text", oldSource: "variables.order.total", newSource: "variables.purchase.total" }],
      flags: [{ nodeId: "switch", inputKey: "Case:big:Condition", reason: "StructureInput", message: "Change it by hand." }]
    });
    const updateState = vi.fn();
    const onSelectReference = vi.fn();
    const host = render(<Harness context={context} draft={renamed} updateState={updateState} onSelectReference={onSelectReference} />);

    flushSync(() => host.querySelector("button")!.click());
    await waitFor(() => expect(host.querySelector("[aria-label='Variable rename']")).not.toBeNull());

    const [path, body] = postJson.mock.calls[0];
    expect(path).toBe("/design/workflows/scoped-variables/rename-plan");
    expect(body).toMatchObject({ scopeId: "workflow", referenceKey: "order-key", newName: "purchase" });
    expect(body.state.variables[0].name).toBe("order");
    const applied = updateState.mock.calls[0][0](renamed) as WorkflowDefinitionState;
    expect((applied.rootActivity!.Text as { expression: { value: string } }).expression.value).toBe("variables.purchase.total");
    const notice = host.querySelector("[aria-label='Variable rename']")!.textContent!;
    expect(notice).toContain("1 script read was updated");
    expect(notice).toContain("Node switch · Input Case:big:Condition");
    expect(notice).toContain("Change it by hand.");
    // The summary is announced through a live region that stays mounted; the list itself is not live.
    expect(host.querySelector("[role='status']")!.textContent).toContain("1 reference needs your attention.");
    expect(host.querySelector("[aria-label='Variable rename']")!.getAttribute("role")).toBeNull();
    const goTo = host.querySelector<HTMLButtonElement>("[aria-label='Go to Node switch, input Input Case:big:Condition']")!;
    flushSync(() => goTo.click());
    expect(onSelectReference).toHaveBeenCalledWith("switch", "Case:big:Condition");
  });

  it("reports a rename whose reads all followed as a notification, without an inline notice", async () => {
    const notify = vi.fn();
    setNotifications({ notify, track: (work: Promise<unknown>) => work } as never);
    try {
      const renamed = state(writeLine("a", "variables.order.total"), [{ ...order, name: "purchase" }]);
      const { context } = api({
        oldName: "order",
        newName: "purchase",
        edits: [{ nodeId: "a", inputKey: "Text", oldSource: "variables.order.total", newSource: "variables.purchase.total" }],
        flags: []
      });
      const updateState = vi.fn();
      const host = render(<Harness context={context} draft={renamed} updateState={updateState} />);

      flushSync(() => host.querySelector("button")!.click());
      await waitFor(() => expect(notify).toHaveBeenCalled());

      expect(notify).toHaveBeenCalledWith({ level: "success", title: "Renamed 'order' to 'purchase'", message: "1 script read was updated." });
      expect(updateState).toHaveBeenCalledTimes(1);
      expect(host.querySelector("[aria-label='Variable rename']")).toBeNull();
    } finally {
      setNotifications(undefined);
    }
  });

  it("plans a second rename after the first, from the scripts the first one updated", async () => {
    const draft = state(writeLine("a", "variables.order.total"), [{ ...order, name: "basket" }]);
    let releaseFirst!: () => void;
    const firstHeld = new Promise<void>(resolve => { releaseFirst = resolve; });
    const getJson = vi.fn().mockResolvedValue({ capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1",
      links: [{ rel: "scoped-variable-rename-plan", href: "design/workflows/scoped-variables/rename-plan" }] }] });
    const postJson = vi.fn(async (_path: string, body: { newName: string; state: WorkflowDefinitionState }) => {
      if (body.newName === "purchase") {
        await firstHeld;
        return { oldName: "order", newName: "purchase", flags: [],
          edits: [{ nodeId: "a", inputKey: "Text", oldSource: "variables.order.total", newSource: "variables.purchase.total" }] };
      }
      const source = (body.state.rootActivity!.inputs[0] as { value: { value: string } }).value.value;
      return { oldName: "purchase", newName: "basket", flags: [],
        edits: [{ nodeId: "a", inputKey: "Text", oldSource: source, newSource: source.replace("purchase", "basket") }] };
    });
    const context = { baseUrl: `test://rename-${Math.random()}`, http: { getJson, postJson } } as never;
    const updateState = vi.fn();
    const host = render(<Harness context={context} draft={draft} updateState={updateState} />);
    const [first, second] = host.querySelectorAll("button");

    flushSync(() => first.click());
    flushSync(() => second.click());
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(postJson).toHaveBeenCalledTimes(1);
    releaseFirst();
    await waitFor(() => expect(updateState).toHaveBeenCalledTimes(2));

    const secondBody = postJson.mock.calls[1][1];
    expect(secondBody.state.variables![0].name).toBe("purchase");
    expect((secondBody.state.rootActivity!.inputs[0] as { value: { value: string } }).value.value).toBe("variables.purchase.total");
    const final = updateState.mock.calls.reduce((current: WorkflowDefinitionState, [producer]) => producer(current), draft) as WorkflowDefinitionState;
    expect((final.rootActivity!.Text as { expression: { value: string } }).expression.value).toBe("variables.basket.total");
  });

  it("reports an edit whose script changed meanwhile instead of applying it", async () => {
    const changed = state(writeLine("a", "variables.order.total + 1"), [{ ...order, name: "purchase" }]);
    const { context } = api({
      oldName: "order",
      newName: "purchase",
      edits: [{ nodeId: "a", inputKey: "Text", oldSource: "variables.order.total", newSource: "variables.purchase.total" }],
      flags: []
    });
    const updateState = vi.fn();
    const host = render(<Harness context={context} draft={changed} updateState={updateState} />);

    flushSync(() => host.querySelector("button")!.click());
    await waitFor(() => expect(host.querySelector("[aria-label='Variable rename']")).not.toBeNull());

    expect(updateState).not.toHaveBeenCalled();
    expect(host.textContent).toContain("The script changed while the rename was planned");
  });

  it("renames the declaration alone when the host does not advertise the plan", async () => {
    const { context, postJson } = api(null, false);
    const updateState = vi.fn();
    const host = render(<Harness context={context} draft={state(writeLine("a", "variables.order"))} updateState={updateState} />);

    flushSync(() => host.querySelector("button")!.click());
    await new Promise(resolve => setTimeout(resolve, 20));

    expect(postJson).not.toHaveBeenCalled();
    expect(updateState).not.toHaveBeenCalled();
    expect(host.querySelector("[aria-label='Variable rename']")).toBeNull();
  });

  it("says the scripts were not updated when planning fails", async () => {
    const { context } = api(new Error("Service unavailable"));
    const host = render(<Harness context={context} draft={state(writeLine("a", "variables.order"))} updateState={vi.fn()} />);

    flushSync(() => host.querySelector("button")!.click());
    await waitFor(() => expect(host.querySelector("[aria-label='Variable rename']")).not.toBeNull());

    expect(host.textContent).toContain("Scripts that read 'order' were not updated: Service unavailable");
  });
});
