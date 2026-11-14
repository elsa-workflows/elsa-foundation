import React, { StrictMode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it } from "vitest";
import { StudioCodeEditor, javaScriptLanguageAdapter } from "@elsa-workflows/studio-code-editor";
import type { StudioActivityDescriptor, StudioExpressionEditorContribution, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import type { ActivityDataSelectionHost } from "../activityDataSelection";
import type { ActivityDataContextResult, ActivityDataSourceQuery } from "../api/activityDataSelection";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import { getDraftContentRevision } from "../workflow-editor/editorHelpers";
import type { WorkflowDraft } from "../workflowTypes";
import { vi } from "vitest";

// This syntax fixture isolates host glue. Its engine/history are real; installed adapter/evaluator and
// actual adapter→publish→run evidence have their own owning tests and permanent composed journeys.
function Surface(props: StudioExpressionEditorProps) {
  return <StudioCodeEditor ariaLabel="Insertion expression" profile={props.context.surface === "expanded" ? "expanded" : "compact"}
    document={{ uri: props.context.document!.uri, language: "javascript", value: String(props.value), version: props.context.document!.sourceVersion }}
    languageAdapter={javaScriptLanguageAdapter} sessionKey={`${props.context.editorSessionScope}\u001f${props.context.document!.id}`}
    documentEditing={props.documentEditing} actions={props.actions} readOnly={props.disabled} focusOnMount={props.initialFocus}
    onFocus={props.context.onFocus} onBlur={props.context.onBlur} onExpand={props.onExpand}
    onChange={(document, edit) => props.onChange(document.value, edit)} />;
}
const contribution: StudioExpressionEditorContribution = { id: "fixture.text", supports: context => context.syntax === "JavaScript",
  sourceInsertion: { render: ({ alias }) => ({ supported: true, insert: `args.${alias}["customerId"]` }) }, surfaces: { inline: Surface, expanded: Surface } };
const descriptor: StudioActivityDescriptor = { typeName: "Writer", inputs: [{ name: "Text", referenceKey: "Text", displayName: "Message", typeName: "System.String", isWrapped: true }], outputs: [], ports: [] };
const parameter = { kind: "ActivityResult" as const, producerNodeId: "fetch", projectionKey: "$result", isOptional: false };
let root: Root, container: HTMLDivElement, doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>;
let ownerContext = {}, readOnly = false, installed = true, changes = 0;
const rangeRects = Object.getOwnPropertyDescriptor(Range.prototype, "getClientRects");
const rangeBounds = Object.getOwnPropertyDescriptor(Range.prototype, "getBoundingClientRect");
const shape = (scalar: boolean) => ({ state: "known" as const, kind: scalar ? "scalar" as const : "object" as const,
  type: { alias: scalar ? "String" : "Customer", collectionKind: "Single" }, sourceContractRevision: "source", profileRevision: "profile", metadataRevision: "metadata",
  isNullable: false, presence: "required" as const, representation: null, isSensitive: false, isCredential: false, reasonCode: null, members: [], element: null, mapValue: null });
const request = vi.fn(async (_target: unknown, _signal: AbortSignal, _skip: number, query?: ActivityDataSourceQuery): Promise<ActivityDataContextResult> => {
  const path = [...query?.memberPath ?? []];
  const selected = shape(path.length > 0);
  const rootShape = path.length ? selected : { ...selected, members: [{ serializedName: "customerId", displayName: "Customer ID", shape: shape(true) }] };
  return { state: "supported", code: null, documentRevision: getDraftContentRevision(doc.state.draft!), baseRevision: "base", snapshotHash: "hash", contextRevision: "context",
    hasMoreSources: false, memberReadsEnabled: false, expressionParametersEnabled: true, expressionType: "JavaScript",
    target: { displayName: "Message", target: { nodeId: "consumer", inputKey: "Text" }, constraint: null, type: { alias: "String", collectionKind: "Single" } },
    sources: [{ id: "activity:fetch:$result", displayName: "Fetch", activityResultSource: parameter, binding: null, reasonCode: "EarlierProducerRun", availability: "definite",
      parameterSource: parameter, parameterPath: path, type: rootShape.type, shape: rootShape }] };
});
function Harness() {
  doc = useWorkflowDocument();
  history = useDraftHistory({ draft: doc.state.draft, restoreDraft: snapshot => doc.editDraft(() => snapshot), transitions: doc.state.historyTransitions, onTransitionsCaptured: doc.acknowledgeHistoryTransitions });
  const draft = doc.state.draft;
  const host: ActivityDataSelectionHost = { ownerContext, expressionInsertion: true, documentRevision: draft ? getDraftContentRevision(draft) : "", baseRevision: "base", saving: false,
    requestSources: request, describeActivity: () => ({ label: "Get Customer", outputLabels: {} }), findingsFor: () => [] };
  return draft ? <ActivityPropertiesPanel draftId={draft.id} activity={draft.state.rootActivity!} workflowState={draft.state} descriptor={descriptor}
    descriptorStatus="ready" expressionDescriptorStatus="ready" editors={[]} expressionEditors={installed ? [contribution] : []}
    expressionDescriptors={[{ type: "JavaScript", displayName: "JavaScript", editingMode: "text" }]} visibleVariables={[]} scopeStatus="ready"
    readOnly={readOnly} expressionEditorSessionScope="insertion-ui" activityDataSelection={host} onChange={(node, edit) => {
      changes++;
      doc.editDraft(({ draft: current }) => ({ ...current!, state: { ...current!.state, rootActivity: node } }),
        edit && edit.origin !== "input" ? { before: edit.before, after: edit.after } : undefined);
    }} /> : null;
}
function render() { flushSync(() => root.render(<StrictMode><Harness /></StrictMode>)); }
beforeEach(() => {
  // jsdom has no text geometry; only the missing DOM APIs are supplied, never editor/history behavior.
  if (!rangeRects) Object.defineProperty(Range.prototype, "getClientRects", { configurable: true, value: () => [] });
  if (!rangeBounds) Object.defineProperty(Range.prototype, "getBoundingClientRect", { configurable: true, value: () => new DOMRect() });
  window.dispatchEvent(new Event("elsa:auth-session-started")); ownerContext = {}; readOnly = false; installed = true; changes = 0; request.mockClear();
  container = document.createElement("div"); document.body.append(container); root = createRoot(container); render();
  const draft: WorkflowDraft = { id: "draft", definitionId: "definition", sourceVersionId: null, validationErrors: [], layout: [], state: { variables: [], inputs: [], outputs: [],
    rootActivity: { nodeId: "consumer", activityVersionId: "writer", inputs: [], outputs: [], Text: { typeName: "System.String", expression: { type: "JavaScript", value: "0" } } } } };
  flushSync(() => { history.resetHistory(draft); doc.loadDraft(draft); });
});
afterEach(() => {
  flushSync(() => root.unmount()); container.remove();
  window.dispatchEvent(new CustomEvent("elsa:expression-editor-session-ended", { detail: { scope: "insertion-ui" } }));
  if (!rangeRects) Reflect.deleteProperty(Range.prototype, "getClientRects");
  if (!rangeBounds) Reflect.deleteProperty(Range.prototype, "getBoundingClientRect");
});
function stored() { return (doc.state.draft!.state.rootActivity!.Text as { expression: { value: unknown } }).expression.value; }
const insertButton = () => document.querySelector<HTMLButtonElement>(".wf-expression-insert-data")!;
const content = () => document.querySelector<HTMLElement>(".cm-content")!;
function cursorOffset() {
  const selection = window.getSelection()!;
  const prefix = document.createRange(); prefix.selectNodeContents(content());
  prefix.setEnd(selection.anchorNode!, selection.anchorOffset);
  return prefix.toString().length;
}
function click(element: HTMLElement) { flushSync(() => element.click()); }
function key(element: HTMLElement, key: string, ctrlKey = false, shiftKey = false) {
  const event = new KeyboardEvent("keydown", { key, ctrlKey, shiftKey, bubbles: true, cancelable: true });
  flushSync(() => element.dispatchEvent(event)); return event.defaultPrevented;
}
async function open() {
  click(insertButton());
  await vi.waitFor(() => expect(document.querySelector(".wf-data-chooser-panel [role='option']")).not.toBeNull());
  await vi.waitFor(() => expect(document.querySelector(".wf-data-chooser-panel")!.contains(document.activeElement)).toBe(true));
}
async function selectMember() {
  const source = [...document.querySelectorAll<HTMLElement>("[role='dialog'] [role='option']")].find(element => element.textContent?.includes("Get Customer"))!;
  click(source);
  await vi.waitFor(() => expect([...document.querySelectorAll("[role='option']")].some(element => element.textContent?.includes("customerId"))).toBe(true));
  click([...document.querySelectorAll<HTMLElement>("[role='option']")].find(element => element.textContent?.includes("customerId"))!);
  await vi.waitFor(() => expect(typeof stored()).toBe("object"));
}
it("activates the real compact engine, focuses the picker, and cancels without a mutation", async () => {
  expect(container.querySelector(".cm-editor")).toBeNull(); await open();
  expect(content()).not.toBeNull(); expect(stored()).toBe("0"); expect(changes).toBe(0);
  key(document.querySelector<HTMLElement>("[role='dialog']")!, "Escape");
  await vi.waitFor(() => expect(document.querySelector("[role='dialog']")).toBeNull());
  await vi.waitFor(() => expect(document.activeElement).toBe(insertButton()));
  expect(changes).toBe(0); expect(history.canUndoNow).toBe(false);
  const outside = document.createElement("button"); document.body.append(outside); outside.focus();
  await vi.waitFor(() => expect(container.querySelector(".cm-editor")).toBeNull()); outside.remove();
});
it("inserts one portable edit through real picker focus and both editor/designer history", async () => {
  click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
  await vi.waitFor(() => expect(content()).not.toBeNull()); content().focus(); key(content(), "a", true);
  insertButton().focus(); await open(); expect(content()).not.toBeNull(); await selectMember();
  expect(stored()).toMatchObject({ source: 'args.data["customerId"]', parameters: { data: parameter } }); expect(changes).toBe(1);
  await vi.waitFor(() => expect(document.activeElement).toBe(content()));
  expect(key(content(), "z", true)).toBe(true); expect(stored()).toBe("0");
  // jsdom's default platform uses Mod-y; Shift-Mod-z is macOS/Linux-specific in the pinned keymap.
  expect(key(content(), "y", true)).toBe(true); expect(stored()).toMatchObject({ parameters: { data: parameter } });
  flushSync(() => history.undo()); expect(stored()).toBe("0");
  flushSync(() => history.redo()); expect(stored()).toMatchObject({ parameters: { data: parameter } });
});
it("transfers the real editor selection/history through compact unmount, expanded mount, and back", async () => {
  click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
  await vi.waitFor(() => expect(content()).not.toBeNull()); content().focus(); key(content(), "a", true);
  await open(); await selectMember();
  const compact = document.querySelector(".cm-editor")!;
  const selected = cursorOffset();
  click(container.querySelector<HTMLButtonElement>("[aria-label='Open expanded Message editor']")!);
  await vi.waitFor(() => expect(document.querySelector(".wf-property-editor-dialog .cm-editor")).not.toBeNull());
  const expanded = document.querySelector(".wf-property-editor-dialog .cm-editor")!;
  expect(expanded).not.toBe(compact); expect(compact.isConnected).toBe(false);
  await vi.waitFor(() => expect(document.activeElement).toBe(content()));
  expect(cursorOffset()).toBe(selected);
  content().focus(); expect(key(content(), "z", true)).toBe(true); expect(stored()).toBe("0");
  await open();
  key(document.querySelector<HTMLElement>(".wf-data-chooser-panel")!, "Escape");
  await vi.waitFor(() => expect(document.querySelector(".wf-data-chooser")).toBeNull());
  await vi.waitFor(() => expect(document.activeElement).toBe(insertButton()));
  click(document.querySelector<HTMLButtonElement>("[aria-label='Close Message editor']")!);
  await vi.waitFor(() => expect(document.querySelector(".wf-property-editor-dialog")).toBeNull());
  await vi.waitFor(() => expect(document.activeElement).toBe(container.querySelector("[aria-label='Open expanded Message editor']")));
  click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
  await vi.waitFor(() => expect(content()).not.toBeNull());
  expect(document.querySelector(".cm-editor")).not.toBe(expanded);
  content().focus(); expect(key(content(), "y", true)).toBe(true);
  expect(stored()).toMatchObject({ source: 'args.data["customerId"]', parameters: { data: parameter } });
});
it("invalidates an already accepted listing on owner-context change, but not a harmless host rerender", async () => {
  await open(); const before = JSON.stringify(doc.state.draft);
  const oldOption = document.querySelector<HTMLElement>("[role='dialog'] [role='option']")!;
  render(); expect(document.querySelector("[role='dialog'] [role='option']")).not.toBeNull();
  ownerContext = {}; render(); click(oldOption);
  await vi.waitFor(() => expect(document.querySelector("[role='dialog']")).toBeNull());
  expect(JSON.stringify(doc.state.draft)).toBe(before); expect(changes).toBe(0);
});

it.each(["read-only", "module-loss", "revoked"])("preserves the owner when an accepted picker loses authority: %s", async scenario => {
  await open(); const before = JSON.stringify(doc.state.draft);
  const oldOption = document.querySelector<HTMLElement>("[role='dialog'] [role='option']")!;
  if (scenario === "read-only") { readOnly = true; render(); }
  else if (scenario === "module-loss") { installed = false; render(); }
  else flushSync(() => window.dispatchEvent(new Event("elsa:auth-session-ended")));
  click(oldOption);
  await vi.waitFor(() => expect(document.querySelector("[role='dialog']")).toBeNull());
  expect(JSON.stringify(doc.state.draft)).toBe(before); expect(changes).toBe(0);
});
