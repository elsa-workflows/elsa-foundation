import { acceptCompletion, closeCompletion, completionKeymap } from "@codemirror/autocomplete";
import {
  defaultKeymap,
  history,
  historyKeymap,
  indentLess,
  indentMore,
  temporarilySetTabFocusMode,
  toggleTabFocusMode
} from "@codemirror/commands";
import { bracketMatching, defaultHighlightStyle, foldGutter, indentOnInput, syntaxHighlighting, syntaxTree } from "@codemirror/language";
import { Compartment, EditorState, Prec, Transaction } from "@codemirror/state";
import { EditorView, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers, type KeyBinding } from "@codemirror/view";
import { useEffect, useRef } from "react";
import { applyCodeMirrorDiagnostics } from "./codeMirrorDiagnostics";
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
import type { StudioCodeDiagnostic, StudioCodeEditorEngineProps } from "../types";

interface CodeMirrorSessionEntry {
  state: EditorState;
  runtime: CodeMirrorRuntime;
  presentation: Compartment;
  editability: Compartment;
  language: Compartment;
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

  const entry = resolveEntry(props);
  entry.runtime.props = props;
  entryRef.current = entry;

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const mountedAuthorizationGeneration = authorizationGeneration;

    const view = claimCompactView(entry, props.profile, container)
      ?? new EditorView({ state: entry.state, parent: container });
    activeCodeMirrorViews.add(view);
    viewRef.current = view;
    view.dispatch({
      effects: entry.presentation.reconfigure(presentationExtensions(props.profile))
    });
    if (props.autoFocus) view.focus();
    applyDiagnostics(view, entry.runtime.props.diagnostics);
    void loadLanguageSupport(view, props.document.language, entry);

    return () => {
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

  useEffect(() => {
    const view = viewRef.current;
    const current = entryRef.current;
    if (!view || !current) return;

    if (props.document.value !== view.state.doc.toString() && props.document.value !== current.runtime.lastEmittedValue) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: props.document.value },
        annotations: Transaction.addToHistory.of(false)
      });
    }
    applyDiagnostics(view, props.diagnostics);
  }, [props.document.value, props.document.version, props.diagnostics]);

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
    if (props.document.value !== existing.state.doc.toString() &&
        props.document.value !== existing.runtime.lastEmittedValue) {
      existing.state = existing.state.update({
        changes: { from: 0, to: existing.state.doc.length, insert: props.document.value },
        annotations: Transaction.addToHistory.of(false)
      }).state;
    }
    return existing;
  }

  const runtime: CodeMirrorRuntime = { props };
  const presentation = new Compartment();
  const editability = new Compartment();
  const language = new Compartment();
  const entry = {} as CodeMirrorSessionEntry;
  const state = EditorState.create({
      doc: props.document.value,
      extensions: [
        history(),
        presentation.of(presentationExtensions(props.profile)),
        editability.of(editabilityExtensions(props.readOnly, props.ariaLabel)),
        language.of([]),
        indentOnInput(),
        bracketMatching(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        Prec.highest(keymap.of(editorKeymap(runtime))),
        createCodeMirrorCodeIntelligenceExtensions({
          document: props.document,
          completionProvider: request => runtime.props.completionProvider
            ? runtime.props.completionProvider(request)
            : runtime.props.completions ?? null,
          hoverProvider: (document, position, signal) => runtime.props.hoverProvider?.(document, position, signal) ?? null,
          signatureProvider: (document, position, signal) => runtime.props.signatureProvider?.(document, position, signal) ?? null
        }),
        EditorView.domEventHandlers({
          focus: () => {
            runtime.props.onFocus?.();
            return false;
          },
          blur: () => {
            runtime.tabEscapeArmed = false;
            runtime.props.onBlur?.();
            return false;
          },
          keydown: event => {
            if (event.key !== "Escape" && event.key !== "Tab") runtime.tabEscapeArmed = false;
            return false;
          }
        }),
        EditorView.updateListener.of(update => handleUpdate(update, entry))
      ]
    });
  entry.runtime = runtime;
  entry.state = state;
  entry.presentation = presentation;
  entry.editability = editability;
  entry.language = language;
  setStudioCodeEditorSessionEntry(props.session, props.document.uri, entry);
  return entry;
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
      run: view => runtime.props.profile === "compact" && (acceptCompletion(view) || requestExpansion(runtime))
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

function handleUpdate(update: { state: EditorState; docChanged: boolean; changes: { iterChanges(callback: (fromA: number, toA: number, fromB: number, toB: number, inserted: { toString(): string }) => void): void } }, entry: CodeMirrorSessionEntry) {
  entry.state = update.state;
  const { runtime } = entry;
  if (!update.docChanged) return;

  let insertedNewline = false;
  update.changes.iterChanges((_fromA, _toA, _fromB, _toB, inserted) => {
    insertedNewline ||= inserted.toString().includes("\n");
  });
  const document = { ...runtime.props.document, value: update.state.doc.toString() };
  runtime.lastEmittedValue = document.value;
  runtime.props.onChange(document);
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
