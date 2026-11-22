// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorContribution, StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { AssignmentEditor } from "../PropertyEditors";

const targetPage = {
  users: [{ id: "alice", name: "Alice" }],
  groups: [{ id: "reviewers", name: "Reviewers" }],
  moreUsers: false,
  moreGroups: false
};

function createApi() {
  const getJson = vi.fn().mockResolvedValue(targetPage);
  const api = {
    backend: { http: { getJson } },
    backendSession: { capabilities: { capabilities: [{
      id: "elsa.api.user-tasks", contractVersion: "1", links: [{ rel: "targets", href: "runtime/user-task-targets" }]
    }] } }
  } as unknown as ElsaStudioModuleApi;
  return { api, getJson };
}

function descriptor(name: string): StudioActivityPropertyEditorProps["descriptor"] {
  return { name, referenceKey: name } as StudioActivityPropertyEditorProps["descriptor"];
}

type AssignmentValues = Record<string, unknown>;
type HarnessProps = {
  api: ElsaStudioModuleApi;
  initial: AssignmentValues;
  disabled?: boolean;
  atomic?: boolean;
  onLiteralInputs?: (values: AssignmentValues) => void;
  onSingleChange?: (key: string, value: unknown) => void;
};

function AssignmentHarness({ api, initial, disabled = false, atomic = true, onLiteralInputs, onSingleChange }: HarnessProps) {
  const [values, setValues] = React.useState(initial);
  const setLiteralInputs = React.useCallback((next: AssignmentValues) => {
    onLiteralInputs?.(next);
    setValues(current => ({ ...current, ...next }));
  }, [onLiteralInputs]);
  const context = {
    activity: {},
    expressionDescriptors: [],
    inputValues: values,
    ...(atomic ? { setLiteralInputs } : {})
  };
  const editor = (group: boolean) => {
    const key = group ? "GroupId" : "AssigneeId";
    return <AssignmentEditor
      key={key}
      api={api}
      group={group}
      descriptor={descriptor(key)}
      value={values[key]}
      disabled={disabled}
      context={context}
      onChange={value => {
        onSingleChange?.(key, value);
        setValues(current => ({ ...current, [key]: value }));
      }}
    />;
  };
  return <>
    {editor(false)}
    {editor(true)}
    <output data-testid="assignment-values">{JSON.stringify(values)}</output>
  </>;
}

async function act(action: () => unknown) {
  let result: unknown;
  flushSync(() => { result = action(); });
  await result;
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(() => root.unmount());
  container.remove();
});

async function render(element: React.ReactNode) {
  await act(() => root.render(element));
}

async function choose(label: string, value: string) {
  const select = container.querySelector<HTMLSelectElement>(`select[aria-label="${label}"]`);
  expect(select).not.toBeNull();
  await act(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
    if (!setter) throw new Error("Select value setter is unavailable");
    setter.call(select, value);
    select!.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function values() {
  return JSON.parse(container.querySelector("[data-testid='assignment-values']")!.textContent!) as AssignmentValues;
}

describe("User Task assignment editor", () => {
  it("keeps a visible alternative marker beside the group picker", async () => {
    const { api } = createApi();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} />);

    expect(container.querySelector(".user-task-assignment-or")?.textContent?.trim()).toBe("or");
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Select an assignee or a group for this task.");
  });

  it("atomically clears the other fixed target when switching in either direction", async () => {
    const { api } = createApi();
    const onLiteralInputs = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} onLiteralInputs={onLiteralInputs} />);

    await choose("Task assignee", "alice");
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: null });
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ AssigneeId: "alice", GroupId: null });
    expect(container.querySelector('[role="alert"]')).toBeNull();

    await choose("Task group", "reviewers");
    expect(values()).toEqual({ AssigneeId: null, GroupId: "reviewers" });
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ GroupId: "reviewers", AssigneeId: null });
    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Task assignee"]')?.value).toBe("");

    await choose("Task assignee", "alice");
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: null });
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ AssigneeId: "alice", GroupId: null });
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("recovers a conflicting fixed assignment by selecting one target", async () => {
    const { api } = createApi();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "old-group" }} />);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Choose either an assignee or a group; clear the other assignment.");

    await choose("Task group", "reviewers");
    expect(values()).toEqual({ AssigneeId: null, GroupId: "reviewers" });
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("disables both pickers in read-only mode", async () => {
    const { api } = createApi();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "" }} disabled />);

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Task assignee"]')?.disabled).toBe(true);
    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Task group"]')?.disabled).toBe(true);
  });

  it("keeps a saved assignment visible while target choices are loading", async () => {
    const { api, getJson } = createApi();
    getJson.mockReturnValue(new Promise(() => {}));
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "" }} />);

    const assignee = container.querySelector<HTMLSelectElement>('select[aria-label="Task assignee"]');
    expect(assignee?.value).toBe("alice");
    expect(assignee?.selectedOptions[0]?.textContent).toBe("alice");
  });

  it("preserves a dynamic sibling assignment when setting a fixed target", async () => {
    const { api } = createApi();
    const expression = { expression: { type: "JavaScript", value: "CurrentUser.Id" } };
    const activity = { inputs: [{ referenceKey: "AssigneeId", value: expression }] };
    const setLiteralInputs = vi.fn();
    const onChange = vi.fn();
    await render(<AssignmentEditor
      api={api}
      group
      descriptor={descriptor("GroupId")}
      value=""
      context={{ activity, inputValues: { GroupId: "" }, expressionDescriptors: [], setLiteralInputs }}
      onChange={onChange}
    />);

    await choose("Task group", "reviewers");
    expect(setLiteralInputs).not.toHaveBeenCalled();
    expect(onChange).toHaveBeenCalledWith("reviewers");
    expect(activity.inputs[0].value).toBe(expression);
  });

  it("uses the existing single-input update on hosts without sibling editing", async () => {
    const { api } = createApi();
    const onSingleChange = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "" }} atomic={false} onSingleChange={onSingleChange} />);

    await choose("Task group", "reviewers");
    expect(onSingleChange).toHaveBeenCalledWith("GroupId", "reviewers");
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: "reviewers" });
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("clear the other assignment");
  });

  it("registers the assignment and form editors as owners of their outlined captions", () => {
    const editors: { id: string; presentsField?: boolean; propertyGroup?: StudioActivityPropertyEditorContribution["propertyGroup"] }[] = [];
    register({
      userTaskForms: { add: vi.fn() },
      featureAreas: { add: vi.fn() },
      propertyEditors: { add: (editor: StudioActivityPropertyEditorContribution) => editors.push(editor) }
    } as unknown as ElsaStudioModuleApi);

    expect(editors.find(editor => editor.id === "user-tasks.user")?.presentsField).toBe(true);
    expect(editors.find(editor => editor.id === "user-tasks.group")?.presentsField).toBe(true);
    expect(editors.find(editor => editor.id === "user-tasks.user")?.propertyGroup).toEqual({
      id: "assignment",
      label: "Assignment",
      description: "Assign this task to one person or one group. Choose an assignee or a group—not both."
    });
    expect(editors.find(editor => editor.id === "user-tasks.group")?.propertyGroup).toEqual(editors.find(editor => editor.id === "user-tasks.user")?.propertyGroup);
    expect(editors.find(editor => editor.id === "user-tasks.form")?.presentsField).toBe(true);
  });
});