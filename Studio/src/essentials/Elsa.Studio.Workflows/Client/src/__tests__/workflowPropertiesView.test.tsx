import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { VariablesEditor, WorkflowPropertiesView } from "../WorkflowPropertiesView";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { clearApiCapabilityCache } from "../api/capabilities";

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

function setSelectValue(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!;
  setter.call(select, value);
  flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
}

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
  setter.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function incidentStrategyContext(
  name: string,
  loadCatalog: () => unknown | Promise<unknown>
) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") {
      return { capabilities: [{
        id: "elsa.api.publishing",
        contractVersion: "1",
        links: [{ rel: "incident-strategies", href: "publishing/incident-strategies" }]
      }] };
    }
    return loadCatalog();
  });
  return {
    context: { baseUrl: `test://${name}`, http: { getJson } } as never,
    getJson
  };
}

const typeOptions = [
  { value: "String", label: "String", group: "Primitives" },
  { value: "Boolean", label: "Boolean", group: "Primitives" },
  { value: "DateTime", label: "Date/Time", group: "Primitives" },
  { value: "Elsa.Http.HttpRequest", label: "HTTP Request", group: "Http" }
];

const editorForAlias = (alias: string) => (alias === "Boolean" ? "checkbox" : alias === "DateTime" ? "date" : "text");

function variable(alias: string, collectionKind = "Single") {
  return { referenceKey: "v1", name: "V", type: { alias, collectionKind }, storageDriverType: null, default: null };
}

describe("argument row editor", () => {
  it("renders the type dropdown grouped by category and a collection-kind dropdown", () => {
    const container = render(
      <VariablesEditor items={[variable("Boolean")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );

    const typeSelect = container.querySelector<HTMLSelectElement>("select[aria-label='Variable type']")!;
    const groups = [...typeSelect.querySelectorAll("optgroup")].map(group => group.label);
    expect(groups).toEqual(["Primitives", "Http"]);
    expect(typeSelect.value).toBe("Boolean");

    const kindSelect = container.querySelector<HTMLSelectElement>("select[aria-label='Variable collection kind']")!;
    expect([...kindSelect.options].map(option => option.value)).toEqual(["Single", "Array", "List", "HashSet"]);
    expect(kindSelect.value).toBe("Single");
  });

  it("renders a type-aware default editor (checkbox for bool, date for datetime)", () => {
    const boolContainer = render(
      <VariablesEditor items={[variable("Boolean")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );
    expect(boolContainer.querySelector("input[aria-label='Variable default value']")?.getAttribute("type")).toBe("checkbox");

    flushSync(() => active!.root.unmount());
    active!.container.remove();
    active = null;

    const dateContainer = render(
      <VariablesEditor items={[variable("DateTime")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );
    expect(dateContainer.querySelector("input[aria-label='Variable default value']")?.getAttribute("type")).toBe("date");
  });

  it("updates only the collection kind, preserving the alias", () => {
    const onChange = vi.fn();
    const container = render(
      <VariablesEditor items={[variable("Boolean")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={onChange} />
    );

    setSelectValue(container.querySelector<HTMLSelectElement>("select[aria-label='Variable collection kind']")!, "Array");

    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next[0].type).toEqual({ alias: "Boolean", collectionKind: "Array" });
  });

  it("surfaces an unknown alias as a disabled option instead of dropping it", () => {
    const container = render(
      <VariablesEditor items={[variable("Some.Unresolved.Type")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );

    const typeSelect = container.querySelector<HTMLSelectElement>("select[aria-label='Variable type']")!;
    const unresolved = [...typeSelect.options].find(option => option.value === "Some.Unresolved.Type");
    expect(unresolved).toBeDefined();
    expect(unresolved!.disabled).toBe(true);
    expect(typeSelect.value).toBe("Some.Unresolved.Type");
  });

  it("falls back to a free-text default for a non-Single collection kind", () => {
    const container = render(
      <VariablesEditor items={[variable("Boolean", "List")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );
    expect(container.querySelector("input[aria-label='Variable default value']")?.getAttribute("type")).toBe("text");
  });

  it("changing the collection kind of a blank-alias row does not invent a 'String' alias", () => {
    const onChange = vi.fn();
    const container = render(
      <VariablesEditor items={[variable("")]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={onChange} />
    );

    setSelectValue(container.querySelector<HTMLSelectElement>("select[aria-label='Variable collection kind']")!, "List");

    const next = onChange.mock.calls[0][0] as Array<Record<string, unknown>>;
    expect(next[0].type).toEqual({ alias: "", collectionKind: "List" });
  });

  it("shows a non-conforming stored default in a text field, not a blanked date input", () => {
    const item = { referenceKey: "v1", name: "When", type: { alias: "DateTime", collectionKind: "Single" }, storageDriverType: null, default: { value: "2026-07-02T00:00:00", expressionType: "Literal" } };
    const container = render(
      <VariablesEditor items={[item]} typeOptions={typeOptions} storageOptions={null} editorForAlias={editorForAlias} onChange={vi.fn()} />
    );

    const input = container.querySelector<HTMLInputElement>("input[aria-label='Variable default value']")!;
    expect(input.getAttribute("type")).toBe("text");
    expect(input.value).toBe("2026-07-02T00:00:00");
  });
});

describe("properties view", () => {
  const rejectingContext = { baseUrl: "", http: { getJson: () => Promise.reject(new Error("404")) } } as never;

  const draft = (): WorkflowDraft => ({
    id: "draft-1",
    definitionId: "def-1",
    state: {
      variables: [],
      rootActivity: { nodeId: "root", activityVersionId: "v1", inputs: [], outputs: [] },
      inputs: [{ name: "In", type: { alias: "String", collectionKind: "Single" }, displayName: "In", storageDriverType: null }],
      outputs: [{ name: "Out", type: { alias: "String", collectionKind: "Single" }, displayName: "Out" }]
    },
    layout: [],
    validationErrors: []
  });

  const details = (): WorkflowDefinitionDetails => ({
    definition: { id: "def-1", name: "My WF", description: "A flow", createdAt: "", lastModifiedAt: "", versionCount: 1 },
    versions: []
  });

  it("shows a collection-kind dropdown on inputs and outputs too", () => {
    const container = render(
      <WorkflowPropertiesView details={details()} draft={draft()} context={rejectingContext} onStateChange={vi.fn()} onDefinitionMetaChange={vi.fn()} />
    );
    expect(container.querySelector("select[aria-label='Input collection kind']")).not.toBeNull();
    expect(container.querySelector("select[aria-label='Output collection kind']")).not.toBeNull();
    // Outputs are minimal — no storage picker.
    expect(container.querySelector("select[aria-label='Output storage driver']")).toBeNull();
  });

  it("requests variable types only through the Expressions capability", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities"
      ? { capabilities: [{
          id: "elsa.api.expressions",
          contractVersion: "1",
          links: [{ rel: "variable-types", href: "expressions/variable-types" }]
        }] }
      : { items: [] });
    const context = { baseUrl: "test://properties", http: { getJson } } as never;

    render(
      <WorkflowPropertiesView details={details()} draft={draft()} context={context} onStateChange={vi.fn()} onDefinitionMetaChange={vi.fn()} />
    );

    await vi.waitFor(() => expect(getJson).toHaveBeenCalledTimes(2));
    expect(getJson).toHaveBeenNthCalledWith(1, "/capabilities");
    expect(getJson).toHaveBeenNthCalledWith(2, "/expressions/variable-types");
  });

  it("pins the exact advertised incident strategy while preserving sibling strategy options", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      activationStrategyType: "Singleton",
      futureOption: { preserve: true },
      incidentStrategy: null
    };
    const { context } = incidentStrategyContext("incident-strategies", () => ({
      items: [{
        alias: "Acme.Operations.Review",
        version: "2026.07",
        displayName: "Operations review",
        description: "Keeps the incident blocking for review."
      }],
      defaultStrategy: { alias: "Fault", version: "1" }
    }));
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("select[aria-label='Incident strategy']")).not.toBeNull());
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Incident strategy']")!;
    const reviewOption = [...select.options].find(option => option.textContent?.includes("Operations review"))!;
    setSelectValue(select, reviewOption.value);

    const producer = onStateChange.mock.calls[0][0] as (state: WorkflowDraft["state"]) => WorkflowDraft["state"];
    expect(producer(workflow.state).strategyOptions).toEqual({
      activationStrategyType: "Singleton",
      futureOption: { preserve: true },
      incidentStrategy: { alias: "Acme.Operations.Review", version: "2026.07" }
    });
  });

  it("uses null for host-default inheritance without losing sibling strategy options", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      activationStrategyType: "Singleton",
      incidentStrategy: { alias: "Fault", version: "1" }
    };
    const { context } = incidentStrategyContext("host-default", () => ({
      items: [{ alias: "Fault", version: "1", displayName: "Fault" }],
      defaultStrategy: { alias: "Fault", version: "1" }
    }));
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("select[aria-label='Incident strategy']")).not.toBeNull());
    setSelectValue(container.querySelector<HTMLSelectElement>("select[aria-label='Incident strategy']")!, "");

    const producer = onStateChange.mock.calls[0][0] as (state: WorkflowDraft["state"]) => WorkflowDraft["state"];
    expect(producer(workflow.state).strategyOptions).toEqual({
      activationStrategyType: "Singleton",
      incidentStrategy: null
    });
  });

  it("keeps an unresolved exact strategy reference selected and disabled", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "Acme.Missing", version: "legacy-v2" }
    };
    const { context } = incidentStrategyContext("unresolved-strategy", () => ({
      items: [{ alias: "Fault", version: "1", displayName: "Fault" }],
      defaultStrategy: { alias: "Fault", version: "1" }
    }));
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("select[aria-label='Incident strategy']")).not.toBeNull());
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Incident strategy']")!;
    const unresolved = [...select.options].find(option => option.textContent?.includes("Acme.Missing / legacy-v2"));
    expect(unresolved).toMatchObject({ disabled: true, selected: true });
    expect(container.textContent).toContain("will be preserved until you choose another option");
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("resolves aliases case-insensitively without rewriting the stored reference", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "fault", version: "1" }
    };
    const { context } = incidentStrategyContext("case-insensitive-strategy", () => ({
      items: [{ alias: "Fault", version: "1", displayName: "Fault", description: "Stops the workflow at the faulting activity." }],
      defaultStrategy: { alias: "Fault", version: "1" }
    }));
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("select[aria-label='Incident strategy']")).not.toBeNull());
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Incident strategy']")!;
    expect(select.selectedOptions[0]?.textContent).toBe("Fault — Fault / 1");
    expect([...select.options].some(option => option.textContent?.includes("(unresolved)"))).toBe(false);
    expect(select.labels[0]?.textContent).toBe("Incident strategy");
    expect(container.querySelector(`#${select.getAttribute("aria-describedby")}`)?.textContent)
      .toContain("Stops the workflow at the faulting activity.");
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("shows the stored reference without mutating it while discovery is loading", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "Acme.Operations.Review", version: "2026.07" }
    };
    const pendingCatalog = new Promise<never>(() => {});
    const { context, getJson } = incidentStrategyContext("loading-strategies", () => pendingCatalog);
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(getJson).toHaveBeenCalledWith("/publishing/incident-strategies"));
    expect(container.querySelector("[role='status']")?.textContent).toContain("Loading incident strategies");
    expect(container.textContent).toContain("Acme.Operations.Review / 2026.07");
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("does not probe a guessed incident-strategy route when the capability relation is unavailable", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "Acme.Stored", version: "v3" }
    };
    const getJson = vi.fn(async (url: string) => url === "/capabilities"
      ? { capabilities: [{
          id: "elsa.api.expressions",
          contractVersion: "1",
          links: [{ rel: "variable-types", href: "expressions/variable-types" }]
        }] }
      : { items: [] });
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={{ baseUrl: "test://strategies-unavailable", http: { getJson } } as never}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.textContent).toContain("discovery is not available"));
    expect(container.textContent).toContain("Acme.Stored / v3");
    expect(getJson).not.toHaveBeenCalledWith(expect.stringContaining("incident-strategies"));
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("preserves the stored selection on request failure and retries discovery explicitly", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "Acme.Stored", version: "v3" }
    };
    let catalogRequests = 0;
    const { context } = incidentStrategyContext("strategies-error", () => {
      catalogRequests += 1;
      if (catalogRequests === 1) throw new Error("Network unavailable");
      return {
        items: [{ alias: "Fault", version: "1", displayName: "Fault" }],
        defaultStrategy: { alias: "Fault", version: "1" }
      };
    });
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("[role='alert']")).not.toBeNull());
    expect(container.textContent).toContain("Acme.Stored / v3");
    click(container.querySelector<HTMLButtonElement>("[role='alert'] button")!);
    await vi.waitFor(() => expect(container.querySelector("select[aria-label='Incident strategy']")).not.toBeNull());
    expect(catalogRequests).toBe(2);
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("keeps the stored selection intact when strategy discovery is permission-denied", async () => {
    const workflow = draft();
    workflow.state.strategyOptions = {
      incidentStrategy: { alias: "Acme.Restricted", version: "1" }
    };
    const { context } = incidentStrategyContext("strategies-forbidden", () => {
      throw Object.assign(new Error("Forbidden"), { status: 403 });
    });
    const onStateChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={workflow}
        context={context}
        onStateChange={onStateChange}
      />
    );

    await vi.waitFor(() => expect(container.querySelector("[role='alert']")).not.toBeNull());
    expect(container.textContent).toContain("Acme.Restricted / 1");
    expect(onStateChange).not.toHaveBeenCalled();
  });

  it("shows an explicit empty state without inventing selectable strategies", async () => {
    const { context } = incidentStrategyContext("strategies-empty", () => ({
      items: [],
      defaultStrategy: { alias: "Fault", version: "1" }
    }));
    const container = render(
      <WorkflowPropertiesView
        details={details()}
        draft={draft()}
        context={context}
        onStateChange={vi.fn()}
      />
    );

    await vi.waitFor(() => expect(container.textContent).toContain("No incident strategies are advertised"));
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='Incident strategy']")!;
    expect([...select.options].map(option => option.textContent)).toEqual([
      "Use host default — Fault / 1"
    ]);
  });

  it("commits a name edit through the metadata handler on blur", () => {
    const onDefinitionMetaChange = vi.fn();
    const container = render(
      <WorkflowPropertiesView details={details()} draft={draft()} context={rejectingContext} onStateChange={vi.fn()} onDefinitionMetaChange={onDefinitionMetaChange} />
    );

    const nameInput = container.querySelector<HTMLInputElement>("input[aria-label='Workflow name']")!;
    expect(nameInput.value).toBe("My WF");
    setInputValue(nameInput, "Renamed WF");
    flushSync(() => nameInput.dispatchEvent(new FocusEvent("focusout", { bubbles: true })));

    expect(onDefinitionMetaChange).toHaveBeenCalledWith({ name: "Renamed WF" });
  });
});
