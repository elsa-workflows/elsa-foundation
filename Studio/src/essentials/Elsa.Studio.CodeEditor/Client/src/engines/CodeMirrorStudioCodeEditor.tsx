import { acceptCompletion, closeCompletion, completionKeymap } from "@codemirror/autocomplete";
import {
  defaultKeymap,
  history,
  historyKeymap,
  isolateHistory,
  indentLess,
  indentMore,
  insertNewlineAndIndent,
  temporarilySetTabFocusMode,
  toggleTabFocusMode
} from "@codemirror/commands";
import { bracketMatching, HighlightStyle, foldGutter, indentOnInput, syntaxHighlighting, syntaxTree } from "@codemirror/language";
import { tags } from "@lezer/highlight";
import { Compartment, EditorState, Prec, Transaction } from "@codemirror/state";
import { EditorView, type ViewUpdate, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers, type KeyBinding } from "@codemirror/view";
import { useEffect, useRef } from "react";
import { applyCodeMirrorDiagnostics } from "./codeMirrorDiagnostics";
import { markCodeEditorReady } from "./markCodeEditorReady";
import {
  cancelCodeMirrorIntelligence,
  createCodeMirrorCodeIntelligenceExtensions,
  dismissCodeMirrorIntelligence,
  requestCodeMirrorKeyboardHover
} from "./codeMirrorCodeIntelligence";
import { loadCodeMirrorLanguageExtensions } from "./codeMirrorLanguages";
import {
  getStudioCodeEditorSessionEntry,
  setStudioCodeEditorSessionEntry,
  subscribeToStudioCodeEditorSessionRevocation
} from "../sessions/studioCodeEditorSessions";
import type { StudioCodeDiagnostic, StudioCodeEditorEngineProps, StudioCodeEditorSnapshot, StudioCodeEditorSelection, StudioCodeEditorBookmark } from "../types";
import { ownerHistoryValue, ownerHistoryInversion, ownerValueKey, setOwnerHistoryValue, snapshotOwnerValue } from "./codeMirrorDocumentEditing";

const studioHighlightStyle = HighlightStyle.define([
  { tag: tags.string, color: "var(--studio-success-fg)" },
  { tag: [tags.number, tags.bool, tags.null], color: "var(--studio-warning-fg)" },
  { tag: [tags.keyword, tags.propertyName, tags.tagName, tags.attributeName], color: "var(--studio-accent)" },
  { tag: tags.comment, color: "var(--studio-text-muted)", fontStyle: "italic" }
]);

interface CodeMirrorSessionEntry {
  state: EditorState;
  runtime: CodeMirrorRuntime;
  presentation: Compartment;
  editability: Compartment;
  language: Compartment;
  revision: number;
  editingEnabled: boolean;
  pendingSelection?: { source: string; key: string; selection: StudioCodeEditorSelection };
}

interface CodeMirrorRuntime {
  props: StudioCodeEditorEngineProps;
  lastEmittedValue?: string;
  tabEscapeArmed?: boolean;
}

// Compact fields are mutually exclusive. Parking the outgoing view until React mounts the incoming
// compact field lets us reparent one CodeMirror DOM tree instead of rebuilding an engine on every
// field switch. A microtask disposal still releases it when focus truly leaves the compact surface.
let parkedCompactView: { view: EditorView; disposeTimer: ReturnType<typeof setTimeout> } | undefined;
const activeCodeMirrorViews = new Set<EditorView>();
let authorizationGeneration = 0;

subscribeToStudioCodeEditorSessionRevocation(() => {
  authorizationGeneration++;
  destroyParkedCompactView();
  for (const view of activeCodeMirrorViews) {
    activeCodeMirrorViews.delete(view);
    destroyCodeMirrorView(view);
  }
});

export function CodeMirrorStudioCodeEditor(props: StudioCodeEditorEngineProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | undefined>(undefined);
  const entryRef = useRef<CodeMirrorSessionEntry | undefined>(undefined);
  const controllerRef = useRef<ReturnType<typeof createEditingController> | undefined>(undefined);

  const entry = resolveEntry(props);
  entry.runtime.props = props;
  entryRef.current = entry;

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const root = container.closest("[data-studio-code-editor]");
    const mountedAuthorizationGeneration = authorizationGeneration;

    const view = claimCompactView(entry, props.profile, container)
      ?? new EditorView({ state: entry.state, parent: container });
    activeCodeMirrorViews.add(view);
    viewRef.current = view;
    container.dataset.studioCodeEditorSurface = "codemirror";
    markCodeEditorReady(container, true);
    view.dispatch({
      effects: entry.presentation.reconfigure(presentationExtensions(props.profile))
    });
    if (props.autoFocus) view.focus();
    const generation = authorizationGeneration;
    const controller = createEditingController(view, entry, generation);
    controllerRef.current = controller;
    applyDiagnostics(view, entry.runtime.props.diagnostics);
    void loadLanguageSupport(view, props.document.language, entry);

    return () => {
      controllerRef.current = undefined;
      markCodeEditorReady(root instanceof HTMLElement ? root : container, false);
      entry.runtime.tabEscapeArmed = false;
      entry.state = view.state;
      setStudioCodeEditorSessionEntry(props.session, props.document.uri, entry);
      if (activeCodeMirrorViews.delete(view)) {
        if (props.profile === "compact" && mountedAuthorizationGeneration === authorizationGeneration) {
          parkCompactView(view);
        } else {
          destroyCodeMirrorView(view);
        }
      }
      viewRef.current = undefined;
    };
  // A session survives profile remounts. A changed URI/session intentionally mounts a new view.
  }, [entry, props.autoFocus, props.document.language, props.document.uri, props.profile, props.session]);

  const registerController = props.documentEditing?.onController;
  useEffect(() => {
    registerController?.(controllerRef.current ?? null);
    return () => registerController?.(null);
  }, [entry, props.profile, registerController]);

  useEffect(() => {
    const view = viewRef.current;
    const current = entryRef.current;
    if (!view || !current) return;
    const latest = current.runtime.props;

    if (current.editingEnabled !== !!latest.documentEditing || latest.documentEditing && (ownerValueKey(latest.documentEditing.historyValue) !== ownerValueKey(view.state.field(ownerHistoryValue, false)) ||
        latest.document.value !== view.state.doc.toString() && latest.document.value !== current.runtime.lastEmittedValue)) {
      current.revision++;
      current.state = createEntryState(latest, current);
      view.setState(current.state);
      void loadLanguageSupport(view, latest.document.language, current);
    } else if (latest.document.value !== view.state.doc.toString() && latest.document.value !== current.runtime.lastEmittedValue) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: latest.document.value },
        annotations: Transaction.addToHistory.of(false)
      });
    }
    applyDiagnostics(view, latest.diagnostics);
  }, [props.document.value, props.document.version, props.documentEditing, props.diagnostics]);

  useEffect(() => {
    const view = viewRef.current;
    const current = entryRef.current;
    if (!view || !current) return;
    view.dispatch({
      effects: current.editability.reconfigure(editabilityExtensions(props.readOnly, props.ariaLabel))
    });
  }, [props.ariaLabel, props.readOnly]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    closeCompletion(view);
    // Signature help is selection-driven. Re-dispatch the current selection when a delayed
    // authoring context replaces the initial local-only providers so the visible document can
    // immediately acquire the newly available signature.
    view.dispatch({ selection: view.state.selection });
  }, [props.completionProvider, props.completions, props.hoverProvider, props.signatureProvider]);

  return (
    <div
      ref={containerRef}
      aria-label={props.ariaLabel}
      className={`studio-code-editor-rich studio-code-editor-rich-${props.profile}`}
      data-profile={props.profile}
      data-theme={props.theme}
      style={{ minHeight: props.minHeight }}
    />
  );
}

function claimCompactView(entry: CodeMirrorSessionEntry, profile: StudioCodeEditorEngineProps["profile"], container: HTMLElement) {
  if (profile !== "compact" || !parkedCompactView) return undefined;
  const { view, disposeTimer } = parkedCompactView;
  clearTimeout(disposeTimer);
  parkedCompactView = undefined;
  // Always install the target entry before reparenting. This replaces document, history, language
  // extensions and event handlers together, so a parked view can never expose the previous field.
  view.setState(entry.state);
  container.append(view.dom);
  return view;
}

function parkCompactView(view: EditorView) {
  if (parkedCompactView) {
    clearTimeout(parkedCompactView.disposeTimer);
    destroyCodeMirrorView(parkedCompactView.view);
  }
  // Detach instead of hiding the DOM: no unfocused field retains a mounted rich editor, while a
  // same-turn or near-immediate field activation can reuse the already initialized engine.
  view.dom.remove();
  const disposeTimer = setTimeout(() => {
    if (parkedCompactView?.view !== view) return;
    parkedCompactView = undefined;
    destroyCodeMirrorView(view);
  }, 1_000);
  parkedCompactView = { view, disposeTimer };
}

export function destroyParkedCompactView() {
  if (!parkedCompactView) return;
  clearTimeout(parkedCompactView.disposeTimer);
  destroyCodeMirrorView(parkedCompactView.view);
  parkedCompactView = undefined;
}

function destroyCodeMirrorView(view: EditorView) {
  // CodeMirror's destroy releases editor resources but intentionally leaves its DOM in place.
  // Removing it first guarantees source disappears synchronously on authorization revocation.
  cancelCodeMirrorIntelligence(view);
  view.dom.remove();
  view.destroy();
}

function resolveEntry(props: StudioCodeEditorEngineProps): CodeMirrorSessionEntry {
  const existing = getStudioCodeEditorSessionEntry<CodeMirrorSessionEntry>(props.session, props.document.uri);
  if (existing) {
    if (props.documentEditing) {
      // Mounted views synchronize in the effect. A remounted profile adopts the current owner before mounting.
      const mounted = [...activeCodeMirrorViews].some(view => view.state === existing.state);
      if (!mounted && (existing.editingEnabled !== !!props.documentEditing || ownerValueKey(props.documentEditing.historyValue) !== ownerValueKey(existing.state.field(ownerHistoryValue, false)) ||
          props.document.value !== existing.state.doc.toString() && props.document.value !== existing.runtime.lastEmittedValue)) {
        existing.revision++;
        existing.state = createEntryState(props, existing);
      }
    } else if (props.document.value !== existing.state.doc.toString() && props.document.value !== existing.runtime.lastEmittedValue) {
      existing.state = existing.state.update({ changes: { from: 0, to: existing.state.doc.length, insert: props.document.value },
        annotations: Transaction.addToHistory.of(false) }).state;
    }
    return existing;
  }

  const runtime: CodeMirrorRuntime = { props };
  const presentation = new Compartment();
  const editability = new Compartment();
  const language = new Compartment();
  const entry = {} as CodeMirrorSessionEntry;
  entry.runtime = runtime;
  entry.presentation = presentation;
  entry.editability = editability;
  entry.language = language;
  entry.revision = 0;
  entry.state = createEntryState(props, entry);
  setStudioCodeEditorSessionEntry(props.session, props.document.uri, entry);
  return entry;
}

function createEntryState(props: StudioCodeEditorEngineProps, entry: CodeMirrorSessionEntry): EditorState {
  entry.editingEnabled = !!props.documentEditing;
  // setState does not notify update listeners. An adopted owner state ends the previous local emission.
  entry.runtime.lastEmittedValue = undefined;
  return EditorState.create({
      doc: props.document.value,
      selection: restoredSelection(props, entry),
      extensions: [
        history(),
        ...(props.documentEditing ? [ownerHistoryValue.init(() => snapshotOwnerValue(props.documentEditing!.historyValue)), ownerHistoryInversion] : []),
        entry.presentation.of(presentationExtensions(props.profile)),
        entry.editability.of(editabilityExtensions(props.readOnly, props.ariaLabel)),
        entry.language.of([]),
        indentOnInput(),
        bracketMatching(),
        syntaxHighlighting(studioHighlightStyle, { fallback: true }),
        Prec.highest(keymap.of(editorKeymap(entry.runtime))),
        createCodeMirrorCodeIntelligenceExtensions({
          document: props.document,
          completionProvider: request => entry.runtime.props.completionProvider
            ? entry.runtime.props.completionProvider(request)
            : entry.runtime.props.completions ?? null,
          hoverProvider: (document, position, signal) => entry.runtime.props.hoverProvider?.(document, position, signal) ?? null,
          signatureProvider: (document, position, signal) => entry.runtime.props.signatureProvider?.(document, position, signal) ?? null
        }),
        EditorView.domEventHandlers({
          focus: () => {
            entry.runtime.props.onFocus?.();
            return false;
          },
          blur: () => {
            entry.runtime.tabEscapeArmed = false;
            entry.runtime.props.onBlur?.();
            return false;
          },
          keydown: event => {
            if (event.key !== "Escape" && event.key !== "Tab") entry.runtime.tabEscapeArmed = false;
            return false;
          }
        }),
        EditorView.updateListener.of(update => handleUpdate(update, entry))
      ]
    });

}

function restoredSelection(props: StudioCodeEditorEngineProps, entry: CodeMirrorSessionEntry) {
  const pending = entry.pendingSelection;
  const selection = pending && pending.source === props.document.value && pending.key === ownerValueKey(props.documentEditing?.historyValue)
    ? pending.selection : entry.state?.selection.main ?? { anchor: 0, head: 0 };
  if (pending) entry.pendingSelection = undefined;
  return { anchor: Math.min(selection.anchor, props.document.value.length), head: Math.min(selection.head, props.document.value.length) };
}

const capturedEdits = new WeakMap<StudioCodeEditorSnapshot, { entry: CodeMirrorSessionEntry; revision: number; generation: number }>();
function createEditingController(view: EditorView, entry: CodeMirrorSessionEntry, generation: number) {
  const available = () => generation === authorizationGeneration && activeCodeMirrorViews.has(view) &&
    !entry.runtime.props.readOnly && !!entry.runtime.props.documentEditing;
  return {
    capture(): StudioCodeEditorSnapshot | null {
      if (!available()) return null;
      const range = view.state.selection.main;
      const snapshot = Object.freeze({ source: view.state.doc.toString(), selection: Object.freeze({ anchor: range.anchor, head: range.head }) });
      capturedEdits.set(snapshot, { entry, revision: entry.revision, generation });
      return snapshot;
    },
    apply(edit: { expected: StudioCodeEditorSnapshot; insert: string; historyValue: unknown; selection?: StudioCodeEditorSelection }) {
      const captured = capturedEdits.get(edit.expected);
      if (!available() || !captured || captured.entry !== entry || captured.revision !== entry.revision || captured.generation !== generation) return false;
      let payload: unknown;
      try { payload = snapshotOwnerValue(edit.historyValue); } catch { return false; }
      const { anchor, head } = edit.expected.selection;
      const from = Math.min(anchor, head), to = Math.max(anchor, head);
      const length = view.state.doc.length - (to - from) + edit.insert.length;
      const selection = edit.selection ?? { anchor: from + edit.insert.length, head: from + edit.insert.length };
      if (![selection.anchor, selection.head].every(position => Number.isInteger(position) && position >= 0 && position <= length)) return false;
      view.dispatch({ changes: { from, to, insert: edit.insert }, selection,
        effects: setOwnerHistoryValue.of(payload), annotations: [isolateHistory.of("full"), Transaction.userEvent.of("input.insert.data")] });
      capturedEdits.delete(edit.expected);
      view.focus();
      return true;
    }
  };
}

function editingBookmark(state: EditorState, entry: CodeMirrorSessionEntry): StudioCodeEditorBookmark {
  const source = state.doc.toString(), key = ownerValueKey(state.field(ownerHistoryValue, false));
  const { anchor, head } = state.selection.main;
  const generation = authorizationGeneration;
  return { restore() {
    if (generation !== authorizationGeneration || entry.runtime.props.readOnly) return;
    entry.pendingSelection = { source, key, selection: { anchor, head } };
    for (const view of activeCodeMirrorViews) if (view.state === entry.state && view.state.doc.toString() === source &&
        ownerValueKey(view.state.field(ownerHistoryValue, false)) === key) {
      view.dispatch({ selection: { anchor, head }, annotations: Transaction.addToHistory.of(false) });
      entry.pendingSelection = undefined;
    }
  } };
}

function presentationExtensions(profile: StudioCodeEditorEngineProps["profile"]) {
  return profile === "expanded" ? [lineNumbers(), foldGutter(), highlightActiveLineGutter(), highlightActiveLine()] : [];
}

function editabilityExtensions(readOnly: boolean, ariaLabel: string) {
  return [
    EditorView.editable.of(!readOnly),
    EditorState.readOnly.of(readOnly),
    EditorView.contentAttributes.of({
      "aria-label": ariaLabel,
      "aria-readonly": String(readOnly)
    })
  ];
}

function editorKeymap(runtime: CodeMirrorRuntime): KeyBinding[] {
  const standard = [...completionKeymap, ...defaultKeymap, ...historyKeymap, { key: "Ctrl-m", run: toggleTabFocusMode }];
  const hoverHelp = ["Ctrl-Shift-h", "F1", "Alt-i"].map(key => ({
    key,
    run: (view: EditorView) => {
      if (!runtime.props.hoverProvider) return false;
      void requestCodeMirrorKeyboardHover(view, {
        document: runtime.props.document,
        hoverProvider: runtime.props.hoverProvider
      });
      return true;
    }
  }));
  return [
    ...hoverHelp,
    {
      key: "Enter",
      run: view => acceptCompletion(view) || (runtime.props.profile === "compact"
        ? requestExpansion(runtime)
        : insertNewlineAndIndent(view))
    },
    {
      key: "Escape",
      run: view => {
        if (dismissCodeMirrorIntelligence(view)) return true;
        runtime.tabEscapeArmed = true;
        return temporarilySetTabFocusMode(view);
      }
    },
    {
      key: "Tab",
      run: view => {
        if (runtime.tabEscapeArmed) {
          runtime.tabEscapeArmed = false;
          return false;
        }
        return indentMore(view);
      }
    },
    {
      key: "Shift-Tab",
      run: indentLess
    },
    ...standard.filter(binding => binding.key !== "Enter" && binding.key !== "Tab" && binding.key !== "Shift-Tab")
  ];
}

function handleUpdate(update: ViewUpdate, entry: CodeMirrorSessionEntry) {
  entry.state = update.state;
  const { runtime } = entry;
  const ownerChanged = update.startState.field(ownerHistoryValue, false) !== update.state.field(ownerHistoryValue, false);
  if (!update.docChanged && !ownerChanged) return;
  entry.revision++;

  let insertedNewline = false;
  update.changes.iterChanges((_fromA, _toA, _fromB, _toB, inserted) => {
    insertedNewline ||= inserted.toString().includes("\n");
  });
  const document = { ...runtime.props.document, value: update.state.doc.toString() };
  runtime.lastEmittedValue = document.value;
  if (runtime.props.documentEditing) {
    const origin = update.transactions.some(transaction => transaction.isUserEvent("undo")) ? "undo"
      : update.transactions.some(transaction => transaction.isUserEvent("redo")) ? "redo"
        : update.transactions.some(transaction => transaction.isUserEvent("input.insert.data")) ? "insertion" : "input";
    runtime.props.onChange(document, { historyValue: update.state.field(ownerHistoryValue), origin,
      before: editingBookmark(update.startState, entry), after: editingBookmark(update.state, entry) });
  } else runtime.props.onChange(document);
  if (insertedNewline) {
    runtime.props.onNewline?.();
    if (runtime.props.profile === "compact") runtime.props.onExpand?.();
  }
}

function requestExpansion(runtime: CodeMirrorRuntime) {
  runtime.props.onExpand?.();
  return true;
}

async function loadLanguageSupport(view: EditorView, language: string, entry: CodeMirrorSessionEntry) {
  const extensions = await loadCodeMirrorLanguageExtensions(language);
  try {
    view.dispatch({ effects: entry.language.reconfigure(extensions) });
    applyDiagnostics(view, entry.runtime.props.diagnostics);
  } catch {
    // The component was unmounted while the optional language chunk was loading.
  }
}

function applyDiagnostics(view: EditorView, supplied: StudioCodeDiagnostic[]) {
  applyCodeMirrorDiagnostics(view, [...supplied, ...localSyntaxDiagnostics(view)]);
}

function localSyntaxDiagnostics(view: EditorView): StudioCodeDiagnostic[] {
  const diagnostics: StudioCodeDiagnostic[] = [];
  syntaxTree(view.state).iterate({
    enter(node) {
      if (!node.type.isError) return;
      const line = view.state.doc.lineAt(node.from);
      diagnostics.push({
        severity: "error",
        code: "STUDIO-SYNTAX",
        message: "Syntax error.",
        startLineNumber: line.number,
        startColumn: node.from - line.from + 1,
        endLineNumber: line.number,
        endColumn: Math.max(node.from - line.from + 2, node.to - line.from + 1)
      });
    }
  });
  return diagnostics;
}
