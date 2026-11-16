import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import type { StudioWeaverClientExecutionResult, StudioWeaverPreparedInvocation } from "@elsa-workflows/studio-sdk";
import { createWeaverRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowWeaver } from "../workflowWeaver";
import { useWorkflowContextBridge } from "../workflow-editor/useWorkflowContextBridge";
import { useWorkflowPageWeaver } from "../workflow-editor/useWorkflowLibraryWeaver";
import {
  applyCreateDialogPrefill, applyListStateChange, buildCreateDialogContent, buildLibraryContent, buildTemplateListContent, createDialogAttachment, createDialogKind, libraryAttachment,
  libraryContextLimit, libraryKind,
  pageBindingProblem, type LibraryContextInput
} from "../workflow-editor/workflowLibraryWeaver";
import type { WorkflowDefinitionBrowseLocation } from "../workflow-editor/workflowDefinitionBrowseLocation";
import type { CreateWorkflowDraft } from "../workflow-editor/editorTypes";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { flowchartActivity, writeLine } from "./fixtures";

// Spec 1193 slice E: the library list, create dialog and template list contexts, and the pages' reviewed working-state changes.
const location: WorkflowDefinitionBrowseLocation = { folderSelection: { id: "folder-1" }, listState: "active", search: " pay ", sort: "modified",
  tagFilters: [{ tagId: "tag-1", operator: "any", valueIds: ["value-1"] }], favoritesOnly: true, columns: ["name", "tags"], viewId: "view-1" };

function input(overrides: Partial<LibraryContextInput> = {}): LibraryContextInput {
  return {
    location, view: { id: "view-1", name: "Finance", shared: true, revision: "v-r1", canEdit: true },
    rows: [{ id: "one", name: "Collect payment", tags: [{ tagId: "tag-1", valueId: "value-1" }], favorite: true }, { id: "two", name: "Refund" }],
    selection: ["one", "hidden"], totalCount: 2, permissions: { canAssignTags: true, canManageTags: false }, organizationEnabled: true, ...overrides
  };
}

describe("workflow library context (spec 1193 FR-020)", () => {
  it("discloses the visible query, view, columns, visible selection and the native flags at the revision of the visible state", () => {
    const content = buildLibraryContent(input());
    expect(content.query).toEqual({ search: "pay", state: "active", folderId: "folder-1", unfiled: false, includeDescendants: false,
      tagFilters: [{ tagId: "tag-1", operator: "any", valueIds: ["value-1"] }], favoritesOnly: true, sort: "modified", publicationState: null });
    expect(content.view).toEqual({ id: "view-1", name: "Finance", ownerKind: "shared", revision: "v-r1", canEdit: true });
    // Bounded to what the user sees: a selected id that is not a visible row is never disclosed.
    expect(content.selection).toEqual(["one"]);
    expect(content.permissions).toEqual({ canAssignTags: true, canManageFolders: false, canManageTags: false, canShareViews: false });
    expect(content.revision).toMatch(/^sha256:[0-9a-f]{64}$/);
    expect(buildLibraryContent(input({ selection: [] })).revision).not.toBe(content.revision);
    expect(buildLibraryContent(input()).revision).toBe(content.revision);
  });

  it("stays well inside the server's 16 KiB bound and the host's 32 KiB attachment budget at its row limit", () => {
    const rows = Array.from({ length: 200 }, (_, index) => ({ id: `definition-${index}`, name: "n".repeat(400),
      tags: Array.from({ length: 20 }, (_, tag) => ({ tagId: `tag-${tag}`, valueId: null })) }));
    const attachment = libraryAttachment(input({ rows, selection: rows.map(row => row.id) }), { clientId: "client-1", generationId: "generation-1" });
    const content = attachment.content as ReturnType<typeof buildLibraryContent> & { itemsOmitted?: number };
    // Over the bound the rows shed their tags, then clip their names, then drop from the end; what was left out is counted.
    expect(JSON.stringify(content).length).toBeLessThan(libraryContextLimit);
    expect(content.items.every(item => item.tags.length === 0 && item.name!.length <= 64)).toBe(true);
    expect(content.items.length + (content.itemsOmitted ?? 0)).toBe(50);
    expect(content.selection).toEqual(content.items.map(item => item.id));
    expect(attachment.target).toEqual({ moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-library", resourceId: "library", revision: content.revision });
    expect(attachment.contentType).toBe(libraryKind);
    // A typical page (50 rows of ordinary names with a tag each) is disclosed whole.
    const typical = libraryAttachment(input({ rows: rows.map(row => ({ ...row, name: "Collect payment", tags: row.tags.slice(0, 1) })) }), { clientId: "c", generationId: "g" });
    expect((typical.content as { items: unknown[] }).items).toHaveLength(50);
    expect(typical.content).not.toHaveProperty("itemsOmitted");
  });

  it("lists templates by identity and persisted revision only, never a definition", () => {
    const content = buildTemplateListContent("appr", "template-1", [{ id: "template-1", name: "Approval", revision: "t-r3" }]);
    expect(content).toMatchObject({ filter: { search: "appr" }, selection: "template-1", templates: [{ id: "template-1", name: "Approval", description: null, revision: "t-r3" }] });
    expect(buildTemplateListContent("", "definition-1", []).selection).toBeNull();
  });
});

describe("bounded page contexts (spec 1193 FR-020, FR-023, FR-024)", () => {
  const long = (id: string) => ({ id, name: `Template ${id} `.padEnd(256, "n"), description: "d".repeat(500), revision: `r-${id}` });
  const serverLimit = 16 * 1024;

  it("clips the disclosed search to the 256 characters the server admits", () => {
    const content = buildLibraryContent(input({ location: { ...location, search: "s".repeat(400) } }));
    expect(content.query.search).toHaveLength(256);
  });

  it("bounds the create dialog's templates to the server's 16 KiB, keeping the draft's chosen template", () => {
    const templates = Array.from({ length: 50 }, (_, index) => long(`t${index}`));
    const draft: CreateWorkflowDraft = { name: "N".repeat(256), description: "D".repeat(2000), rootActivityVersionId: null, templateId: "t49" };
    const content = buildCreateDialogContent(draft, templates);
    expect(JSON.stringify(content).length).toBeLessThanOrEqual(serverLimit);
    expect(content.templates.some(template => template.id === "t49")).toBe(true);
    expect(Object.keys(content).sort()).toEqual(["description", "name", "revision", "rootActivityVersionId", "templateId", "templates"]);
    // Descriptions go before templates do.
    expect(content.templates.length).toBeGreaterThan(30);
    expect(content.templates.every(template => template.description === null || template.description.length <= 120)).toBe(true);
  });

  it("leaves a dialog with few templates whole", () => {
    const templates = Array.from({ length: 3 }, (_, index) => long(`t${index}`));
    const content = buildCreateDialogContent({ name: "N", description: "", rootActivityVersionId: null }, templates);
    expect(content.templates.map(template => template.description)).toEqual(templates.map(template => template.description));
  });

  it("bounds the template list to the server's 16 KiB, keeping the selected template and its selection", () => {
    // Long identities, so that even without descriptions and with clipped names not every template fits.
    const id = (index: number) => `t${index}-`.padEnd(120, "x");
    const templates = Array.from({ length: 100 }, (_, index) => long(id(index)));
    const content = buildTemplateListContent("", id(99), templates);
    expect(JSON.stringify(content).length).toBeLessThanOrEqual(serverLimit);
    expect(content.selection).toBe(id(99));
    expect(content.templates.some(template => template.id === id(99))).toBe(true);
    // Left out from the end, the selected template last: the kept ones are the first ones, then the selection.
    expect(content.templates.length).toBeLessThan(100);
    expect(content.templates.slice(0, -1).map(template => template.id)).toEqual(templates.slice(0, content.templates.length - 1).map(template => template.id));
    expect(content.templates.every(template => template.description === null)).toBe(true);
    expect(Object.keys(content).sort()).toEqual(["filter", "revision", "selection", "templates"]);
  });
});

describe("page working-state changes (spec 1193 FR-020, FR-023)", () => {
  it("applies only the supported list-state fields and clears the applied view", () => {
    expect(applyListStateChange(location, { search: "invoice", sort: "name-desc", columns: ["name", "favorite"], folderId: null })).toMatchObject({
      search: "invoice", sort: "name-desc", columns: ["name", "favorite"], folderSelection: "all", viewId: undefined });
    expect(applyListStateChange(location, { delete: true })).toBeNull();
    expect(applyListStateChange(location, { sort: "random" })).toBeNull();
    expect(applyListStateChange(location, {})).toBeNull();
  });

  it("prefills the create dialog with a root activity or a listed template, never creating anything", () => {
    const draft: CreateWorkflowDraft = { name: "", description: "", rootActivityVersionId: null };
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", description: "New hires", rootActivityVersionId: "flowchart-v1" }, []))
      .toEqual({ name: "Onboard", description: "New hires", rootActivityVersionId: "flowchart-v1", metadataEdited: { name: true, description: true } });
    const templates = [{ id: "template-1", name: "Approval", revision: "t-r3", content: { state: { rootActivity: null }, layout: [], activityPresentation: [] } as never }];
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-1" }, templates)).toMatchObject({ templateId: "template-1", rootActivityVersionId: null });
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-9" }, templates)).toBeNull();
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-1", rootActivityVersionId: "x" }, templates)).toBeNull();
  });

  it("refuses a change bound to another page binding or an older revision", () => {
    const snapshot = libraryAttachment(input(), { clientId: "client-1", generationId: "generation-1" });
    const invocation = (overrides: Partial<StudioWeaverPreparedInvocation> = {}) => ({ invocationId: "i-1", client: { clientId: "client-1", generationId: "generation-1" },
      target: snapshot.target, expiresAt: new Date(Date.now() + 60_000).toISOString(), arguments: {}, ...overrides }) as StudioWeaverPreparedInvocation;
    expect(pageBindingProblem(invocation(), snapshot, "workflow-library")).toBeNull();
    expect(pageBindingProblem(invocation({ client: { clientId: "client-1", generationId: "other" } }), snapshot, "workflow-library")).toContain("stale");
    expect(pageBindingProblem(invocation({ target: { ...snapshot.target!, revision: "sha256:old" } }), snapshot, "workflow-library")).toContain("changed after review");
    expect(pageBindingProblem(invocation(), null, "workflow-library")).toContain("no longer open");
  });

  it("applies an approved list change on its own page, returns the exact change in the receipt and reverts it", async () => {
    const weaverRegistry = createWeaverRegistry();
    const controller = registerWorkflowWeaver(weaverRegistry);
    let state: WorkflowDefinitionBrowseLocation = location;
    function Page() {
      const [current, setCurrent] = useState(location);
      state = current;
      useWorkflowPageWeaver<WorkflowDefinitionBrowseLocation>({
        controller, clientId: weaverRegistry.clientId, kind: libraryKind, toolId: "workflow_library_set_list_state", resultType: "workflows.list-state", describe: "changed",
        build: (value, client) => libraryAttachment(input({ location: value }), client), apply: change => applyListStateChange(current, change),
        current: () => current, restore: setCurrent
      });
      return null;
    }
    const root = createRoot(document.createElement("div"));
    flushSync(() => root.render(<Page />));
    const [snapshotKey, snapshot] = [...controller.library!.snapshots].find(([key]) => key.startsWith(libraryKind + ":"))!;
    const contract = weaverRegistry.toolContracts.list().find(item => item.id === "workflow_library_set_list_state")!;
    const change = { search: "invoice" };
    const invocation = { invocationId: "i-1", client: snapshot.client, target: snapshot.target, expiresAt: new Date(Date.now() + 60_000).toISOString(),
      arguments: { baseRevision: snapshot.target!.revision, change } } as unknown as StudioWeaverPreparedInvocation;
    let result: StudioWeaverClientExecutionResult | undefined;
    await new Promise<void>(resolve => flushSync(() => { void contract.executeClient!(invocation).then(value => { result = value; resolve(); }); }));
    flushSync(() => undefined);
    expect(result!.outcome).toBe("applied");
    const data = result!.result!.data as { change: unknown; baseRevision: string; revision: string; undoToken: string };
    expect(data.change).toEqual(change);
    expect(data.baseRevision).toBe(snapshot.target!.revision);
    expect(data.revision).toBe(result!.after!.revision);
    expect(state.search).toBe("invoice");
    // A second dispatch of the same reviewed change is stale: the page's revision moved on.
    const again = await contract.executeClient!(invocation);
    expect(again.outcome).toBe("refused");
    flushSync(() => { expect(controller.library!.revert!(data.undoToken)).toBe("Reverted the change on this page."); });
    expect(state.search).toBe(" pay ");
    expect(controller.library!.revert!(data.undoToken)).toContain("can no longer be reverted");
    flushSync(() => root.unmount());
    expect(controller.library!.snapshots.has(snapshotKey)).toBe(false);
  });
});

describe("two mounted pages of one kind (spec 1193 FR-023)", () => {
  it("keep their own contexts and handlers: unmounting one never removes the other's", async () => {
    const weaverRegistry = createWeaverRegistry();
    const controller = registerWorkflowWeaver(weaverRegistry);
    const states: Record<string, CreateWorkflowDraft> = {};
    function Dialog({ id }: { id: string }) {
      const [draft, setDraft] = useState<CreateWorkflowDraft>({ name: id, description: "", rootActivityVersionId: null });
      states[id] = draft;
      useWorkflowPageWeaver<CreateWorkflowDraft | null>({
        controller, clientId: weaverRegistry.clientId, kind: createDialogKind, toolId: "workflow_create_dialog_prefill", resultType: "workflows.create-dialog-prefill", describe: "filled",
        build: (value, client) => value ? createDialogAttachment(value, [], client) : null, apply: change => applyCreateDialogPrefill(draft, change, []),
        current: () => draft, restore: value => { if (value) setDraft(value); }
      });
      return null;
    }
    const first = createRoot(document.createElement("div")), second = createRoot(document.createElement("div"));
    flushSync(() => first.render(<Dialog id="library" />));
    flushSync(() => second.render(<Dialog id="templates" />));
    const dialogs = () => [...controller.library!.snapshots].filter(([key]) => key.startsWith(createDialogKind + ":")).map(([, value]) => value);
    expect(dialogs()).toHaveLength(2);
    const libraryDialog = dialogs().find(item => (item.content as { name: string }).name === "library")!;
    flushSync(() => second.unmount());
    expect(dialogs()).toEqual([libraryDialog]);
    const contract = weaverRegistry.toolContracts.list().find(item => item.id === "workflow_create_dialog_prefill")!;
    const invocation = { invocationId: "i-2", client: libraryDialog.client, target: libraryDialog.target, expiresAt: new Date(Date.now() + 60_000).toISOString(),
      arguments: { baseRevision: libraryDialog.target!.revision, change: { name: "Onboard" } } } as unknown as StudioWeaverPreparedInvocation;
    let result: StudioWeaverClientExecutionResult | undefined;
    await new Promise<void>(resolve => flushSync(() => { void contract.executeClient!(invocation).then(value => { result = value; resolve(); }); }));
    flushSync(() => undefined);
    expect(result!.outcome).toBe("applied");
    expect(states.library.name).toBe("Onboard");
    flushSync(() => first.unmount());
  });
});

describe("template editor working state (spec 1193 FR-024)", () => {
  it("is disclosed as its own resource with its persisted revision, never as a workflow definition", () => {
    const weaverRegistry = createWeaverRegistry();
    const controller = registerWorkflowWeaver(weaverRegistry);
    const draft = { id: "template-1", definitionId: "template-1", state: { rootActivity: { nodeId: "root", activityVersionId: flowchartActivity.activityVersionId, inputs: [], outputs: [] } },
      layout: [], activityPresentation: [], validationErrors: [] } as unknown as WorkflowDraft;
    const catalog = new Map([writeLine, flowchartActivity].map(item => [item.activityVersionId, item]));
    function Probe() {
      useWorkflowContextBridge({ controller, clientId: weaverRegistry.clientId, details: { definition: { id: "template-1", name: "Approval" } } as WorkflowDefinitionDetails, draft,
        selectedNode: null, selectedNodeId: null, selectedDescriptor: null, inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion: catalog,
        weaverResource: { resourceType: "workflow-template", contentType: "workflows.template-editor", revision: () => "t-r3" } });
      return null;
    }
    const root = createRoot(document.createElement("div"));
    flushSync(() => root.render(<Probe />));
    const snapshot = controller.snapshot!;
    flushSync(() => root.unmount());
    expect(snapshot.contentType).toBe("workflows.template-editor");
    expect(snapshot.target).toMatchObject({ resourceType: "workflow-template", resourceId: "template-1" });
    const content = snapshot.content as Record<string, unknown>;
    expect(content).toMatchObject({ templateId: "template-1", templateRevision: "t-r3" });
    expect(content).not.toHaveProperty("workflowDefinitionId");
    expect(content).not.toHaveProperty("draftId");
  });
});
