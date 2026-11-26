import { useEffect, useState } from "react";
import {
  StudioCodeEditor,
  createStudioCodeEditorSession,
  clearStudioCodeEditorSession,
  jsonLanguageAdapter,
  xmlLanguageAdapter,
  htmlLanguageAdapter,
  plainTextLanguageAdapter,
  javaScriptLanguageAdapter
} from "@elsa-workflows/studio-code-editor";
import { getHttpContentTypeLanguage } from "./httpContentTypeLanguage";

const languageAdapters = {
  json: jsonLanguageAdapter,
  xml: xmlLanguageAdapter,
  html: htmlLanguageAdapter,
  javascript: javaScriptLanguageAdapter,
  plaintext: plainTextLanguageAdapter
};

export function HttpMessageBodyEditor({ label, contentType, value, readOnly = false, onChange }: {
  label: string;
  contentType: string;
  value: string;
  readOnly?: boolean;
  onChange?(value: string): void;
}) {
  const [session] = useState(createStudioCodeEditorSession);
  useEffect(() => () => {
    // The child saves its state on unmount; release that state after its cleanup finishes.
    queueMicrotask(() => clearStudioCodeEditorSession(session));
  }, [session]);
  const language = getHttpContentTypeLanguage(contentType);
  return <StudioCodeEditor
    ariaLabel={label}
    document={{ uri: `elsa://http-test/${encodeURIComponent(label)}`, language, value }}
    session={session}
    languageAdapter={languageAdapters[language]}
    minHeight="160px"
    readOnly={readOnly}
    onChange={next => onChange?.(next.value)}
  />;
}
