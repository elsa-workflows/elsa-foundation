import type { ChangeEvent } from "react";
import type { StudioCodeDiagnostic, StudioCodeEditorProps } from "./types";

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
  const languageLabel = languageAdapter?.displayName ?? document.language;

  const handleChange = (event: ChangeEvent<HTMLTextAreaElement>) => {
    if (readOnly) return;
    onChange({ ...document, value: event.target.value });
  };

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
      <textarea
        aria-label={ariaLabel}
        aria-readonly={readOnly}
        className="studio-code-editor-input"
        readOnly={readOnly}
        spellCheck={false}
        autoCapitalize="off"
        autoCorrect="off"
        value={document.value}
        style={{ minHeight }}
        onChange={handleChange}
      />
      <StudioCodeDiagnostics diagnostics={visibleDiagnostics} />
    </section>
  );
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
