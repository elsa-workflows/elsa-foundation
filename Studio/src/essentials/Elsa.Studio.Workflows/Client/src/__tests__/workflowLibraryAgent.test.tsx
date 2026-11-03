import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import type { StudioAgentClientExecutionResult, StudioAgentPreparedInvocation } from "@elsa-workflows/studio-sdk";
import { createAgentRegistry } from "@elsa-workflows/studio-sdk";
import { registerWorkflowAgent } from "../workflowAgent";
import { useWorkflowContextBridge } from "../workflow-editor/useWorkflowContextBridge";
import { useWorkflowPageAgent } from "../workflow-editor/useWorkflowLibraryAgent";
import {
  applyCreateDialogPrefill, applyListStateChange, buildLibraryContent, buildTemplateListContent, libraryAttachment, libraryContextLimit, libraryKind,
  pageBindingProblem, type LibraryContextInput
} from "../workflow-editor/workflowLibraryAgent";
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
      .toEqual({ name: "Onboard", description: "New hires", rootActivityVersionId: "flowchart-v1" });
    const templates = [{ id: "template-1", name: "Approval", revision: "t-r3", content: { state: { rootActivity: null }, layout: [], activityPresentation: [] } as never }];
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-1" }, templates)).toMatchObject({ templateId: "template-1", rootActivityVersionId: null });
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-9" }, templates)).toBeNull();
    expect(applyCreateDialogPrefill(draft, { name: "Onboard", templateId: "template-1", rootActivityVersionId: "x" }, templates)).toBeNull();
  });

  it("refuses a change bound to another page binding or an older revision", () => {
    const snapshot = libraryAttachment(input(), { clientId: "client-1", generationId: "generation-1" });
    const invocation = (overrides: Partial<StudioAgentPreparedInvocation> = {}) => ({ invocationId: "i-1", client: { clientId: "client-1", generationId: "generation-1" },
      target: snapshot.target, expiresAt: new Date(Date.now() + 60_000).toISOString(), arguments: {}, ...overrides }) as StudioAgentPreparedInvocation;
    expect(pageBindingProblem(invocation(), snapshot, "workflow-library")).toBeNull();
    expect(pageBindingProblem(invocation({ client: { clientId: "client-1", generationId: "other" } }), snapshot, "workflow-library")).toContain("stale");
    expect(pageBindingProblem(invocation({ target: { ...snapshot.target!, revision: "sha256:old" } }), snapshot, "workflow-library")).toContain("changed after review");
    expect(pageBindingProblem(invocation(), null, "workflow-library")).toContain("no longer open");
  });

  it("applies an approved list change on its own page, returns the exact change in the receipt and reverts it", async () => {
    const agent = createAgentRegistry();
    const controller = registerWorkflowAgent(agent);
    let state: WorkflowDefinitionBrowseLocation = location;
    function Page() {
      const [current, setCurrent] = useState(location);
      state = current;
      useWorkflowPageAgent<WorkflowDefinitionBrowseLocation>({
        controller, clientId: agent.clientId, kind: libraryKind, toolId: "workflow_library_set_list_state", resultType: "workflows.list-state", describe: "changed",
        build: (value, client) => libraryAttachment(input({ location: value }), client), apply: change => applyListStateChange(current, change),
        current: () => current, restore: setCurrent
      });
      return null;
    }
    const root = createRoot(document.createElement("div"));
    flushSync(() => root.render(<Page />));
    const snapshot = controller.library!.snapshots.get(libraryKind)!;
    const contract = agent.toolContracts.list().find(item => item.id === "workflow_library_set_list_state")!;
    const change = { search: "invoice" };
    const invocation = { invocationId: "i-1", client: snapshot.client, target: snapshot.target, expiresAt: new Date(Date.now() + 60_000).toISOString(),
      arguments: { baseRevision: snapshot.target!.revision, change } } as unknown as StudioAgentPreparedInvocation;
    let result: StudioAgentClientExecutionResult | undefined;
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
    expect(controller.library!.snapshots.has(libraryKind)).toBe(false);
  });
});

describe("template editor working state (spec 1193 FR-024)", () => {
  it("is disclosed as its own resource with its persisted revision, never as a workflow definition", () => {
    const agent = createAgentRegistry();
    const controller = registerWorkflowAgent(agent);
    const draft = { id: "template-1", definitionId: "template-1", state: { rootActivity: { nodeId: "root", activityVersionId: flowchartActivity.activityVersionId, inputs: [], outputs: [] } },
      layout: [], activityPresentation: [], validationErrors: [] } as unknown as WorkflowDraft;
    const catalog = new Map([writeLine, flowchartActivity].map(item => [item.activityVersionId, item]));
    function Probe() {
      useWorkflowContextBridge({ controller, clientId: agent.clientId, details: { definition: { id: "template-1", name: "Approval" } } as WorkflowDefinitionDetails, draft,
        selectedNode: null, selectedNodeId: null, selectedDescriptor: null, inspectedNode: null, inspectedDescriptor: null, inspectedIsScopeOwner: false, catalogByVersion: catalog,
        agentResource: { resourceType: "workflow-template", contentType: "workflows.template-editor", revision: () => "t-r3" } });
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
