import { lazy, Suspense } from "react";
import type { StudioCodeDiagnostic, StudioCodeEditorProps } from "./types";
import { FallbackCodeEditor } from "./engines/FallbackCodeEditor";
import { getBuiltInLanguageAdapter } from "./languages/builtInLanguageAdapters";

const RichCodeEditor = lazy(() => import("./engines/CodeMirrorStudioCodeEditor").then(module => ({
  default: module.CodeMirrorStudioCodeEditor
})));

export function StudioCodeEditor({
  document,
  diagnostics = [],
  readOnly = false,
  theme = "studio",
  minHeight = "220px",
  ariaLabel,
  languageAdapter,
  onChange
}: StudioCodeEditorProps) {
  const visibleDiagnostics = diagnostics.filter(diagnostic => !diagnostic.uri || diagnostic.uri === document.uri);
  const resolvedLanguageAdapter = languageAdapter ?? getBuiltInLanguageAdapter(document.language);
  const languageLabel = resolvedLanguageAdapter?.displayName ?? document.language;
  const useRichEditor = supportsRichEditor(document.language);

  return (
    <section
      className="studio-code-editor"
      data-language={document.language}
      data-theme={theme}
      data-readonly={readOnly}
    >
      <div className="studio-code-editor-header">
        <span>{languageLabel}</span>
        <code>{document.uri}</code>
      </div>
      {useRichEditor ? (
        <Suspense fallback={
          <FallbackCodeEditor
            document={document}
            readOnly={readOnly}
            minHeight={minHeight}
            ariaLabel={ariaLabel}
            onChange={onChange}
          />
        }>
          <RichCodeEditor
            document={document}
            readOnly={readOnly}
            theme={theme}
            minHeight={minHeight}
            ariaLabel={ariaLabel}
            onChange={onChange}
          />
        </Suspense>
      ) : (
        <FallbackCodeEditor
          document={document}
          readOnly={readOnly}
          minHeight={minHeight}
          ariaLabel={ariaLabel}
          onChange={onChange}
        />
      )}
      <StudioCodeDiagnostics diagnostics={visibleDiagnostics} />
    </section>
  );
}

function supportsRichEditor(language: string) {
  const normalized = language.trim().toLowerCase();
  return normalized === "javascript" || normalized === "typescript";
}

function StudioCodeDiagnostics({ diagnostics }: { diagnostics: StudioCodeDiagnostic[] }) {
  if (diagnostics.length === 0) return null;

  return (
    <div className="studio-code-editor-diagnostics" role="status">
      {diagnostics.map((diagnostic, index) => {
        const severity = diagnostic.severity ?? "info";
        const location = formatLocation(diagnostic);
        return (
          <p
            className={`studio-code-editor-diagnostic ${severity}`}
            key={`${diagnostic.uri ?? "document"}-${diagnostic.code ?? "diagnostic"}-${index}`}
          >
            {diagnostic.code ? <span>{diagnostic.code}</span> : null}
            {location ? <small>{location}</small> : null}
            {diagnostic.message}
          </p>
        );
      })}
    </div>
  );
}

function formatLocation(diagnostic: StudioCodeDiagnostic) {
  if (!diagnostic.startLineNumber) return null;
  return diagnostic.startColumn
    ? `${diagnostic.startLineNumber}:${diagnostic.startColumn}`
    : String(diagnostic.startLineNumber);
}
