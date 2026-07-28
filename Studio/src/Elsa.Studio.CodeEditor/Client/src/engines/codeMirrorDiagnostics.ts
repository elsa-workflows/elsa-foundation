import { setDiagnostics, type Diagnostic } from "@codemirror/lint";
import type { EditorView } from "@codemirror/view";
import type { StudioCodeDiagnostic } from "../types";

export function applyCodeMirrorDiagnostics(view: EditorView, diagnostics: StudioCodeDiagnostic[]) {
  view.dispatch(setDiagnostics(view.state, diagnostics.map(diagnostic => toCodeMirrorDiagnostic(view, diagnostic))));
}

function toCodeMirrorDiagnostic(view: EditorView, diagnostic: StudioCodeDiagnostic): Diagnostic {
  const from = offsetAt(view, diagnostic.startLineNumber, diagnostic.startColumn);
  const to = offsetAt(view, diagnostic.endLineNumber ?? diagnostic.startLineNumber, diagnostic.endColumn ?? diagnostic.startColumn) || from;
  return {
    from,
    to,
    severity: diagnostic.severity === "info" ? "info" : diagnostic.severity ?? "info",
    message: diagnostic.message,
    source: diagnostic.code
  };
}

function offsetAt(view: EditorView, lineNumber = 1, column = 1) {
  const line = view.state.doc.line(Math.min(Math.max(1, lineNumber), view.state.doc.lines));
  return Math.min(line.from + Math.max(0, column - 1), line.to);
}
