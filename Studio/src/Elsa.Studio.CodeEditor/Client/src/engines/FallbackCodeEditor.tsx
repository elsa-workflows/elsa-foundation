import type { ChangeEvent } from "react";
import type { StudioCodeDocument } from "../types";

export interface FallbackCodeEditorProps {
  document: StudioCodeDocument;
  readOnly: boolean;
  minHeight: string;
  ariaLabel: string;
  onChange(document: StudioCodeDocument): void;
}

export function FallbackCodeEditor({
  document,
  readOnly,
  minHeight,
  ariaLabel,
  onChange
}: FallbackCodeEditorProps) {
  const handleChange = (event: ChangeEvent<HTMLTextAreaElement>) => {
    if (readOnly) return;
    onChange({ ...document, value: event.target.value });
  };

  return (
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
  );
}
