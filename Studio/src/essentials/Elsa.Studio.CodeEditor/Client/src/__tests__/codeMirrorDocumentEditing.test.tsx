import { StrictMode } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { EditorView } from "@codemirror/view";
import { Transaction } from "@codemirror/state";
import { undo, redo } from "@codemirror/commands";
import { afterEach, beforeEach, expect, it } from "vitest";
import { CodeMirrorStudioCodeEditor } from "../engines/CodeMirrorStudioCodeEditor";
import { createStudioCodeEditorSession } from "../sessions/studioCodeEditorSessions";
import type { StudioCodeEditorController, StudioCodeEditorEdit, StudioCodeEditorEngineProps } from "../types";

let close: () => void;
beforeEach(() => window.dispatchEvent(new Event("elsa:auth-session-started")));
afterEach(() => close?.());
function editor() {
  const host = document.createElement("div"); document.body.append(host);
  const root = createRoot(host);
  let controller: StudioCodeEditorController | null = null;
  const edits: StudioCodeEditorEdit[] = [];
  let value: unknown = { parameters: {} };
  const register = (next: StudioCodeEditorController | null) => { controller = next; };
  let props: StudioCodeEditorEngineProps = {
    document: { uri: "elsa://transaction", language: "text", value: "a" },
    documentEditing: { historyValue: value, onController: register },
    session: createStudioCodeEditorSession(), profile: "compact", readOnly: false, theme: "studio",
    minHeight: "20px", ariaLabel: "Expression", diagnostics: [],
    onChange(document, edit) {
      if (edit) { value = edit.historyValue; edits.push(edit); }
      props = { ...props, document, documentEditing: { historyValue: value, onController: register } };
      render();
    }
  };
  let mountKey = 0, mounted = true;
  function render() { if (mounted) flushSync(() => root.render(<StrictMode><CodeMirrorStudioCodeEditor key={mountKey} {...props} /></StrictMode>)); }
  render(); close = () => { flushSync(() => root.unmount()); host.remove(); };
  const view = () => EditorView.findFromDOM(host.querySelector(".cm-editor")!)!;
  return {
    park() { mounted = false; flushSync(() => root.render(null)); },
    remount(profile: "compact" | "expanded") { flushSync(() => root.render(null)); mounted = true; mountKey++; props = { ...props, profile }; render(); },
    view, edits, payload: () => value, controller: () => controller!,
    change(next: Partial<StudioCodeEditorEngineProps>) { props = { ...props, ...next }; render(); },
    replace(payload: unknown, source = props.document.value) { value = payload; props = { ...props, document: { ...props.document, value: source }, documentEditing: { historyValue: value, onController: register } }; render(); },
    type(text: string) { const v = view(); flushSync(() => v.dispatch({ changes: { from: v.state.selection.main.head, insert: text }, selection: { anchor: v.state.selection.main.head + text.length }, annotations: Transaction.userEvent.of("input.type") })); }
  };
}
const bound = { parameters: { customer: { kind: "ActivityResult", producerNodeId: "fetch", projectionKey: "$result", isOptional: false } } };
it("histories real typing and an isolated source/payload/selection insertion together", () => {
  const e = editor(); e.view().dispatch({ selection: { anchor: 1 } }); e.type("b");
  const original = structuredClone(bound);
  expect(e.controller().apply({ expected: e.controller().capture()!, insert: "+args.customer", historyValue: original })).toBe(true);
  original.parameters.customer.producerNodeId = "mutated-outside-history";
  expect(e.payload()).toEqual(bound);
  e.type("!");
  flushSync(() => { expect(undo(e.view())).toBe(true); });
  expect(e.view().state.doc.toString()).toBe("ab+args.customer"); expect(e.payload()).toEqual(bound);
  flushSync(() => { expect(undo(e.view())).toBe(true); });
  expect(e.view().state.doc.toString()).toBe("ab"); expect(e.payload()).toEqual({ parameters: {} });
  expect(e.view().state.selection.main.head).toBe(2);
  flushSync(() => { expect(undo(e.view())).toBe(true); }); expect(e.view().state.doc.toString()).toBe("a");
  flushSync(() => { redo(e.view()); redo(e.view()); });
  expect(e.view().state.doc.toString()).toBe("ab+args.customer"); expect(e.payload()).toEqual(bound);
  expect(e.edits.map(edit => edit.origin)).toContain("insertion");
});
it("retains alias-reuse history across compact/expanded and back", () => {
  const e = editor(); e.replace(bound); e.view().dispatch({ selection: { anchor: 1 } });
  expect(e.controller().apply({ expected: e.controller().capture()!, insert: "+args.customer", historyValue: bound })).toBe(true);
  e.remount("expanded");
  expect(e.view().state.selection.main.head).toBe(15);
  flushSync(() => { undo(e.view()); }); expect(e.payload()).toEqual(bound); expect(e.view().state.doc.toString()).toBe("a");
  e.remount("compact"); flushSync(() => { redo(e.view()); });
  expect(e.payload()).toEqual(bound); expect(e.view().state.doc.toString()).toBe("a+args.customer");
});
it("restores a designer bookmark on external Undo/Redo before subsequent local typing", () => {
  const e = editor(); e.view().dispatch({ selection: { anchor: 1 } });
  expect(e.controller().apply({ expected: e.controller().capture()!, insert: "+args.customer", historyValue: bound })).toBe(true);
  const insertion = e.edits.at(-1)!;
  insertion.before.restore(); e.replace({ parameters: {} }, "a");
  expect(e.view().state.selection.main.head).toBe(1); expect(redo(e.view())).toBe(false);
  insertion.after.restore(); e.replace(bound, "a+args.customer");
  expect(e.view().state.selection.main.head).toBe(15);
  e.type("!"); flushSync(() => { undo(e.view()); });
  expect(e.payload()).toEqual(bound); expect(e.view().state.doc.toString()).toBe("a+args.customer");
});
it.each([
  ["mounted", "raw typing"], ["remounted", "raw typing"],
  ["mounted", "reused alias"], ["remounted", "reused alias"]
])("adopts same-payload designer Undo/Redo before typing: %s / %s", (surface, operation) => {
  const e = editor(), payload = operation === "reused alias" ? bound : { parameters: {} };
  e.replace(payload); e.view().dispatch({ selection: { anchor: 1 } });
  if (operation === "reused alias") expect(e.controller().apply({ expected: e.controller().capture()!, insert: "+args.customer", historyValue: payload })).toBe(true);
  else e.type("b");
  const after = operation === "reused alias" ? "a+args.customer" : "ab";
  const edit = e.edits.at(-1)!;
  if (surface === "remounted") e.park();
  edit.before.restore(); e.replace(payload, "a");
  if (surface === "remounted") e.remount("expanded");
  expect(e.view().state.doc.toString()).toBe("a"); expect(e.view().state.selection.main.head).toBe(1);
  edit.after.restore(); e.replace(payload, after);
  const adopted = { source: e.view().state.doc.toString(), caret: e.view().state.selection.main.head };
  e.type("!");
  expect(e.view().state.doc.toString()).toBe(after + "!");
  expect(adopted).toEqual({ source: after, caret: after.length });
  expect(e.payload()).toEqual(payload);
  flushSync(() => { expect(undo(e.view())).toBe(true); });
  expect(e.view().state.doc.toString()).toBe(after); expect(e.view().state.selection.main.head).toBe(after.length); expect(e.payload()).toEqual(payload);
});
it("invalidates local history on same-source external binding replacement, then histories new typing", () => {
  const e = editor(); e.replace(bound);
  expect(e.controller().apply({ expected: e.controller().capture()!, insert: "", historyValue: { parameters: {} } })).toBe(true);
  flushSync(() => { undo(e.view()); }); expect(e.payload()).toEqual(bound);
  const stale = e.controller().capture()!;
  const replacement = { parameters: { customer: { ...bound.parameters.customer, producerNodeId: "other" } } };
  e.replace(replacement);
  expect(e.controller().apply({ expected: stale, insert: "bad", historyValue: bound })).toBe(false);
  expect(redo(e.view())).toBe(false); expect(undo(e.view())).toBe(false);
  e.type("!"); flushSync(() => { undo(e.view()); });
  expect(e.view().state.doc.toString()).toBe("a"); expect(e.payload()).toEqual(replacement);
});
it("rejects stale, read-only, switched-target and revoked controllers without an edit", () => {
  const e = editor(); const controller = e.controller(); const initial = controller.capture()!;
  e.type("b"); expect(controller.apply({ expected: initial, insert: "bad", historyValue: bound })).toBe(false);
  const current = controller.capture()!; e.change({ readOnly: true });
  expect(controller.capture()).toBeNull(); expect(controller.apply({ expected: current, insert: "bad", historyValue: bound })).toBe(false);
  e.change({ readOnly: false, document: { uri: "elsa://other", language: "text", value: "other" } });
  expect(controller.apply({ expected: current, insert: "bad", historyValue: bound })).toBe(false);
  const latest = e.controller(), latestSnapshot = latest.capture()!;
  flushSync(() => window.dispatchEvent(new Event("elsa:auth-session-ended")));
  expect(latest.capture()).toBeNull(); expect(latest.apply({ expected: latestSnapshot, insert: "bad", historyValue: bound })).toBe(false);
  expect(e.edits).toHaveLength(1);
});
