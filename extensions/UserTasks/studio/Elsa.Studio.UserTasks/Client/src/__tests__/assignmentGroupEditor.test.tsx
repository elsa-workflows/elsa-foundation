// @vitest-environment jsdom
import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type {
  ElsaStudioModuleApi,
  StudioActivityPropertyEditorGroupInput,
  StudioActivityPropertyEditorGroupProps
} from "@elsa-workflows/studio-sdk";
import { AssignmentGroupEditor } from "../PropertyEditors";

const targetPage = {
  users: [{ id: "alice", name: "Alice" }],
  groups: [{ id: "alice", name: "Alice Reviewers" }, { id: "reviewers", name: "Reviewers" }],
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

type AssignmentValues = Record<string, unknown>;
type HarnessProps = {
  api: ElsaStudioModuleApi;
  initial: AssignmentValues;
  disabled?: boolean;
  atomic?: boolean;
  onLiteralInputs?: (values: AssignmentValues) => void;
  onSingleChange?: (key: string, value: unknown) => void;
  onValidationChange?: (hasError: boolean) => void;
};

function makeInput(key: string, rawValue: unknown, readOnly: boolean): StudioActivityPropertyEditorGroupInput {
  const expression = rawValue && typeof rawValue === "object" && "syntax" in rawValue
    ? rawValue as { syntax: string; source?: unknown }
    : null;
  return {
    descriptor: { name: key, referenceKey: key, displayName: key } as StudioActivityPropertyEditorGroupInput["descriptor"],
    value: expression ? expression.source : rawValue,
    syntax: expression?.syntax ?? "Literal",
    expressionValue: expression?.source,
    readOnly
  };
}

function AssignmentHarness({ api, initial, disabled = false, atomic = true, onLiteralInputs, onSingleChange, onValidationChange }: HarnessProps) {
  const [values, setValues] = React.useState(initial);
  const setLiteralInputs = React.useCallback((next: AssignmentValues) => {
    onLiteralInputs?.(next);
    setValues(current => ({ ...current, ...next }));
  }, [onLiteralInputs]);
  const groupInputs = [makeInput("AssigneeId", values.AssigneeId, disabled), makeInput("GroupId", values.GroupId, disabled)];
  const props: StudioActivityPropertyEditorGroupProps = {
    inputs: groupInputs,
    context: {
      activity: {},
      expressionDescriptors: [],
      inputValues: values,
      readOnly: disabled,
      ...(atomic ? { setLiteralInputs } : {})
    },
    disabled,
    renderExpressionEditor: key => <output data-expression-editor={key}>Host expression editor: {key}</output>,
    onChange: (key, value) => {
      onSingleChange?.(key, value);
      setValues(current => ({ ...current, [key]: value }));
    }
  };
  return <>
    <AssignmentGroupEditor {...props} api={api} onValidationChange={onValidationChange} />
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

async function click(label: string) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent?.trim() === label);
  expect(button).toBeDefined();
  await act(() => button!.click());
}

function values() {
  return JSON.parse(container.querySelector("[data-testid='assignment-values']")!.textContent!) as AssignmentValues;
}

describe("User Task unified assignment editor", () => {
  it("uses one kind-qualified picker for people and groups", async () => {
    const { api } = createApi();
    const onValidationChange = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} onValidationChange={onValidationChange} />);

    expect(container.querySelectorAll("select")).toHaveLength(1);
    const assignTo = container.querySelector<HTMLSelectElement>('select[aria-label="Assign to"]');
    expect(assignTo).not.toBeNull();
    expect(assignTo?.querySelector<HTMLOptionElement>('optgroup[label="People"] option')?.value).toBe("person:alice");
    expect(assignTo?.querySelector('optgroup[label="Groups"] option[value="group:alice"]')?.textContent).toContain("Alice Reviewers");
    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Select a person or a group");
    expect(container.querySelector('[data-expression-editor]')).toBeNull();
    expect(onValidationChange).toHaveBeenLastCalledWith(true);
  });

  it("atomically switches between person and group and can clear the assignment", async () => {
    const { api } = createApi();
    const onLiteralInputs = vi.fn();
    const onValidationChange = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} onLiteralInputs={onLiteralInputs} onValidationChange={onValidationChange} />);

    await choose("Assign to", "person:alice");
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: null });
    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Assign to"]')?.selectedOptions[0]?.textContent).toBe("Alice · Person");
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ AssigneeId: "alice", GroupId: null });
    expect(onValidationChange).toHaveBeenLastCalledWith(false);

    await choose("Assign to", "group:reviewers");
    expect(values()).toEqual({ AssigneeId: null, GroupId: "reviewers" });
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ AssigneeId: null, GroupId: "reviewers" });

    await choose("Assign to", "");
    expect(values()).toEqual({ AssigneeId: null, GroupId: null });
  });

  it("requires an explicit keep action to recover conflicting legacy assignments and preserves the kept expression", async () => {
    const { api } = createApi();
    const source = { syntax: "JavaScript", source: "currentGroup()" };
    const onLiteralInputs = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: source }} onLiteralInputs={onLiteralInputs} />);

    expect(container.querySelector('[role="alert"]')?.textContent).toMatch(/both/i);
    expect(container.querySelector('select[aria-label="Assign to"]')).toBeNull();
    expect(container.querySelectorAll(".user-task-assignment-recovery button")).toHaveLength(2);
    await click("Keep group assignment");
    expect(onLiteralInputs).toHaveBeenLastCalledWith({ AssigneeId: null });
    expect(values()).toEqual({ AssigneeId: null, GroupId: source });
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("opens expression setup without changing the saved target and can cancel it", async () => {
    const { api } = createApi();
    const onLiteralInputs = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "" }} onLiteralInputs={onLiteralInputs} />);

    await click("Use an expression");
    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Expression returns"]')?.value).toBe("AssigneeId");
    expect(container.querySelector('[data-expression-editor="AssigneeId"]')).not.toBeNull();
    await choose("Expression returns", "GroupId");
    expect(container.querySelector('[data-expression-editor="GroupId"]')).not.toBeNull();
    expect(onLiteralInputs).not.toHaveBeenCalled();
    await click("Cancel expression setup");
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: "" });
    expect(container.querySelector('[data-expression-editor]')).toBeNull();
  });

  it("keeps unresolved saved targets visible while choices load", async () => {
    const { api, getJson } = createApi();
    getJson.mockReturnValue(new Promise(() => {}));
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "missing-user", GroupId: "" }} />);

    const assignTo = container.querySelector<HTMLSelectElement>('select[aria-label="Assign to"]');
    expect(assignTo?.value).toBe("person:missing-user");
    expect(assignTo?.selectedOptions[0]?.textContent).toContain("missing-user");
  });

  it("keeps a configured expression assignment visible even when its source is empty", async () => {
    const { api } = createApi();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: { syntax: "JavaScript" } }} />);

    expect(container.querySelector('select[aria-label="Assign to"]')).toBeNull();
    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Expression returns"]')?.value).toBe("GroupId");
    expect(container.querySelector('[data-expression-editor="GroupId"]')).not.toBeNull();
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("loads additional people and groups together without duplicating previously loaded choices", async () => {
    const { api, getJson } = createApi();
    getJson.mockResolvedValueOnce({
      users: [{ id: "alice", name: "Alice" }],
      groups: [{ id: "team-1", name: "Team One" }],
      moreUsers: true,
      moreGroups: true
    }).mockResolvedValueOnce({
      users: [{ id: "bob", name: "Bob" }],
      groups: [{ id: "team-2", name: "Team Two" }],
      moreUsers: false,
      moreGroups: false
    });
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} />);

    expect(container.querySelectorAll('option[value="person:alice"]')).toHaveLength(1);
    await click("More choices");

    expect(getJson).toHaveBeenCalledTimes(2);
    expect(String(getJson.mock.calls[1][0])).toContain("skip=100&take=100");
    expect(container.querySelector('option[value="person:bob"]')?.textContent).toContain("Bob · Person");
    expect(container.querySelector('option[value="group:team-2"]')?.textContent).toContain("Team Two · Group");
    expect(container.querySelectorAll('option[value="person:alice"]')).toHaveLength(1);
    expect([...container.querySelectorAll("button")].some(button => button.textContent?.trim() === "More choices")).toBe(false);
  });

  it("offers retry when the combined target request fails", async () => {
    const { api, getJson } = createApi();
    getJson.mockRejectedValueOnce(new Error("Target request failed"));
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} />);

    expect([...container.querySelectorAll('[role="alert"]')].some(alert => alert.textContent?.includes("Target request failed"))).toBe(true);
    await click("Retry loading people and groups");
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(container.querySelector('option[value="person:alice"]')).not.toBeNull();
    expect([...container.querySelectorAll('[role="alert"]')].some(alert => alert.textContent?.includes("Target request failed"))).toBe(false);
  });

  it("disables the unified controls in read-only mode", async () => {
    const { api } = createApi();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "", GroupId: "" }} disabled />);

    expect(container.querySelector<HTMLSelectElement>('select[aria-label="Assign to"]')?.disabled).toBe(true);
    expect([...container.querySelectorAll("button")].every(button => button.disabled)).toBe(true);
  });

  it("does not switch to a second target when atomic sibling editing is unavailable", async () => {
    const { api } = createApi();
    const onSingleChange = vi.fn();
    await render(<AssignmentHarness api={api} initial={{ AssigneeId: "alice", GroupId: "" }} atomic={false} onSingleChange={onSingleChange} />);

    await choose("Assign to", "group:reviewers");
    expect(onSingleChange).not.toHaveBeenCalled();
    expect(values()).toEqual({ AssigneeId: "alice", GroupId: "" });
    expect(container.querySelector('[role="status"]')?.textContent).toContain("cannot be changed safely");
  });
});