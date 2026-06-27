import { useEffect, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import type { Extension } from "@codemirror/state";
import type { StudioCodeEditorProps } from "../types";
import { loadCodeMirrorLanguageExtensions } from "./codeMirrorLanguages";

export function CodeMirrorStudioCodeEditor({
  document,
  readOnly = false,
  theme = "studio",
  minHeight = "220px",
  ariaLabel,
  onChange
}: StudioCodeEditorProps) {
  const [extensions, setExtensions] = useState<Extension[]>([]);

  useEffect(() => {
    let cancelled = false;
    void loadCodeMirrorLanguageExtensions(document.language).then(nextExtensions => {
      if (!cancelled) setExtensions(nextExtensions);
    });

    return () => {
      cancelled = true;
    };
  }, [document.language]);

  return (
    <CodeMirror
      aria-label={ariaLabel}
      className="studio-code-editor-rich"
      value={document.value}
      minHeight={minHeight}
      basicSetup={{
        lineNumbers: true,
        foldGutter: true,
        highlightActiveLine: true,
        bracketMatching: true,
        autocompletion: true
      }}
      editable={!readOnly}
      readOnly={readOnly}
      theme={theme === "dark" ? "dark" : "light"}
      extensions={extensions}
      onChange={nextValue => {
        if (!readOnly) onChange({ ...document, value: nextValue });
      }}
    />
  );
}
