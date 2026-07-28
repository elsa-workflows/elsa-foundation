import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { FallbackCodeEditor } from "./engines/FallbackCodeEditor";
import {
  getStudioCodeEditorSession,
  subscribeToStudioCodeEditorSessionRevocation
} from "./sessions/studioCodeEditorSessions";
import type { StudioCodeDiagnostic, StudioCodeEditorEngineProps, StudioCodeEditorProps } from "./types";

let activeCompactSession: string | undefined;
const compactEditorSubscribers = new Set<(activeSession: string | undefined) => void>();

function activateCompactSession(session: string) {
  if (activeCompactSession === session) return;
  activeCompactSession = session;
  compactEditorSubscribers.forEach(notify => notify(session));
}

function releaseCompactSession(session: string) {
  if (activeCompactSession !== session) return;
  activeCompactSession = undefined;
  compactEditorSubscribers.forEach(notify => notify(undefined));
}

function subscribeToCompactSessions(notify: (activeSession: string | undefined) => void) {
  compactEditorSubscribers.add(notify);
  return () => {
    compactEditorSubscribers.delete(notify);
  };
}

export function StudioCodeEditor({
  document,
  profile = "expanded",
  sessionKey,
  session: suppliedSession,
  diagnostics = [],
  completions,
  completionProvider,
  hoverProvider,
  signatureProvider,
  readOnly = false,
  focusOnMount,
  theme = "studio",
  minHeight = "220px",
  ariaLabel,
  status,
  escapeDescription,
  languageAdapter,
  onChange,
  onFocus,
  onBlur,
  onExpand,
  onNewline
}: StudioCodeEditorProps) {
  const [compactActive, setCompactActive] = useState(profile === "expanded");
  const [authorizationRevoked, setAuthorizationRevoked] = useState(false);
  const compactSession = sessionKey ?? document.uri;
  useEffect(
    () => subscribeToStudioCodeEditorSessionRevocation(() => setAuthorizationRevoked(true)),
    []
  );
  useEffect(() => {
    if (profile !== "compact") return;
    return subscribeToCompactSessions(activeSession => setCompactActive(activeSession === compactSession));
  }, [compactSession, profile]);
  useEffect(() => () => releaseCompactSession(compactSession), [compactSession]);
  const visibleDiagnostics = diagnostics.filter(diagnostic => !diagnostic.uri || diagnostic.uri === document.uri);
  const languageLabel = languageAdapter?.displayName ?? document.language;
  const loadEditor = languageAdapter?.loadEditor;
  const RichCodeEditor = useMemo(() => loadEditor ? lazy(loadEditor) : null, [loadEditor]);
  const session = useMemo(
    () => suppliedSession ?? getStudioCodeEditorSession(sessionKey ?? document.uri),
    [suppliedSession, sessionKey, document.uri]
  );
  const isCompactPreview = profile === "compact" && !compactActive;
  const engineProps: StudioCodeEditorEngineProps = {
    document,
    profile,
    session,
    readOnly,
    theme,
    minHeight,
    ariaLabel,
    autoFocus: profile === "compact" || focusOnMount === true,
    diagnostics: visibleDiagnostics,
    completions,
    completionProvider,
    hoverProvider,
    signatureProvider,
    onChange,
    onFocus: () => {
      if (profile === "compact") activateCompactSession(compactSession);
      onFocus?.();
    },
    onBlur: () => {
      onBlur?.();
      if (profile === "compact") releaseCompactSession(compactSession);
    },
    onExpand,
    onNewline
  };

  return (
    <section
      className={`studio-code-editor studio-code-editor-${profile}`}
      data-studio-code-editor="true"
      data-language={document.language}
      data-profile={profile}
      data-theme={theme}
      data-readonly={readOnly}
      onBlurCapture={event => {
        if (profile !== "compact") return;
        // CodeMirror may hand focus directly to another compact preview while its lazy editor is
        // resolving. Observe the section boundary as well as the engine callback so a blurred
        // compact editor always returns to its lightweight preview.
        const editorElement = event.currentTarget;
        queueMicrotask(() => {
          if (!editorElement.contains(globalThis.document.activeElement)) releaseCompactSession(compactSession);
        });
      }}
    >
      {profile === "expanded" && !authorizationRevoked ? (
        <div className="studio-code-editor-header">
          <span>{languageLabel}</span>
          <code>{document.uri}</code>
        </div>
      ) : null}
      {authorizationRevoked ? (
        <div className="studio-code-editor-status" role="status">
          Expression source is hidden because the authorization session changed.
        </div>
      ) : isCompactPreview ? (
        <button
          type="button"
          className="studio-code-editor-preview"
          aria-label={`${ariaLabel}. Activate to edit.`}
          onClick={() => activateCompactSession(compactSession)}
          onFocus={() => activateCompactSession(compactSession)}
        >
          <code>{previewValue(document.value)}</code>
          {document.value.includes("\n") ? <span aria-hidden="true">↗</span> : null}
        </button>
      ) : RichCodeEditor ? (
        <Suspense fallback={<FallbackCodeEditor {...engineProps} />}>
          <RichCodeEditor {...engineProps} />
        </Suspense>
      ) : (
        <FallbackCodeEditor {...engineProps} />
      )}
      {!authorizationRevoked && status ? <div className="studio-code-editor-status" role="status" aria-live="polite">{status}</div> : null}
      {!authorizationRevoked ? <div className="studio-code-editor-escape" aria-live="polite">
        {escapeDescription ?? defaultEscapeDescription(profile)}
      </div> : null}
      {!authorizationRevoked ? <StudioCodeDiagnostics diagnostics={visibleDiagnostics} profile={profile} /> : null}
    </section>
  );
}

function StudioCodeDiagnostics({ diagnostics, profile }: { diagnostics: StudioCodeDiagnostic[]; profile: "compact" | "expanded" }) {
  if (diagnostics.length === 0) return null;
  const visible = profile === "compact" ? [highestPriorityDiagnostic(diagnostics)] : diagnostics;

  return (
    <div className="studio-code-editor-diagnostics" role="status" aria-live="polite">
      {visible.map((diagnostic, index) => {
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

function previewValue(value: string) {
  return value.replace(/\n/g, " ↵ ") || "Expression";
}

function highestPriorityDiagnostic(diagnostics: StudioCodeDiagnostic[]) {
  return [...diagnostics].sort((left, right) => severityRank(right.severity) - severityRank(left.severity))[0]!;
}

function severityRank(severity: StudioCodeDiagnostic["severity"]) {
  return severity === "error" ? 3 : severity === "warning" ? 2 : 1;
}

function defaultEscapeDescription(profile: "compact" | "expanded") {
  return profile === "compact"
    ? "Tab indents. Control Shift H shows hover help. Press Escape, then Tab, or Control M to move focus out. Enter expands when a completion is not selected."
    : "Tab indents. Control Shift H shows hover help. Press Escape, then Tab, or Control M to move focus out of the editor.";
}

function formatLocation(diagnostic: StudioCodeDiagnostic) {
  if (!diagnostic.startLineNumber) return null;
  return diagnostic.startColumn
    ? `${diagnostic.startLineNumber}:${diagnostic.startColumn}`
    : String(diagnostic.startLineNumber);
}
