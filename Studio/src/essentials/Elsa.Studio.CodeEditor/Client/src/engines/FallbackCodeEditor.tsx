import { useEffect, useRef, type ChangeEvent, type KeyboardEvent } from "react";
import type { StudioCodeEditorEngineProps } from "../types";

/** A safe generic editor when a language module or rich editor chunk is unavailable. */
export function FallbackCodeEditor({
  document,
  profile,
  readOnly,
  minHeight,
  ariaLabel,
  autoFocus,
  onChange,
  onFocus,
  onBlur,
  onExpand,
  onNewline
}: StudioCodeEditorEngineProps) {
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const tabEscapeArmed = useRef(false);

  useEffect(() => {
    if (autoFocus) inputRef.current?.focus();
  }, [autoFocus]);

  const handleChange = (event: ChangeEvent<HTMLTextAreaElement>) => {
    if (readOnly) return;
    const value = event.target.value;
    onChange({ ...document, value });
    if (value.includes("\n") && !document.value.includes("\n")) {
      onNewline?.();
      if (profile === "compact") onExpand?.();
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.ctrlKey && event.key.toLowerCase() === "m") {
      event.preventDefault();
      tabEscapeArmed.current = true;
      return;
    }
    if (event.key === "Escape") {
      tabEscapeArmed.current = true;
      return;
    }
    if (event.key === "Tab") {
      if (tabEscapeArmed.current) {
        tabEscapeArmed.current = false;
        return;
      }
      event.preventDefault();
      if (readOnly) return;
      applyTabIndent(event.currentTarget, event.shiftKey, document, onChange);
      return;
    }
    tabEscapeArmed.current = false;
    if (profile === "compact" && event.key === "Enter") {
      event.preventDefault();
      onExpand?.();
    }
  };

  return (
    <textarea
      ref={inputRef}
      aria-label={ariaLabel}
      aria-readonly={readOnly}
      className={`studio-code-editor-input studio-code-editor-input-${profile}`}
      readOnly={readOnly}
      spellCheck={false}
      autoCapitalize="off"
      autoCorrect="off"
      value={document.value}
      style={{ minHeight: profile === "compact" ? undefined : minHeight }}
      onBlur={onBlur}
      onChange={handleChange}
      onFocus={onFocus}
      onKeyDown={handleKeyDown}
    />
  );
}

function applyTabIndent(
  input: HTMLTextAreaElement,
  outdent: boolean,
  document: StudioCodeEditorEngineProps["document"],
  onChange: StudioCodeEditorEngineProps["onChange"]
) {
  const start = input.selectionStart;
  const end = input.selectionEnd;
  if (start !== end) {
    const lineStart = document.value.lastIndexOf("\n", Math.max(0, start - 1)) + 1;
    const selectionEnd = end > start && document.value[end - 1] === "\n" ? end - 1 : end;
    const nextLineBreak = document.value.indexOf("\n", selectionEnd);
    const blockEnd = nextLineBreak < 0 ? document.value.length : nextLineBreak;
    const block = document.value.slice(lineStart, blockEnd);
    const lines = block.split("\n");
    const transformed = outdent
      ? lines.map(line => line.replace(/^(?: {1,2}|\t)/, ""))
      : lines.map(line => `  ${line}`);
    const replacement = transformed.join("\n");
    const removedFromFirstLine = lines[0]!.length - transformed[0]!.length;
    const totalDelta = replacement.length - block.length;
    const value = `${document.value.slice(0, lineStart)}${replacement}${document.value.slice(blockEnd)}`;
    onChange({ ...document, value });
    const nextStart = outdent ? Math.max(lineStart, start - removedFromFirstLine) : start + 2;
    const nextEnd = Math.max(nextStart, end + totalDelta);
    queueMicrotask(() => input.setSelectionRange(nextStart, nextEnd));
    return;
  }

  if (!outdent) {
    const value = `${document.value.slice(0, start)}  ${document.value.slice(start)}`;
    onChange({ ...document, value });
    queueMicrotask(() => input.setSelectionRange(start + 2, start + 2));
    return;
  }

  const lineStart = document.value.lastIndexOf("\n", Math.max(0, start - 1)) + 1;
  const removable = document.value.slice(lineStart, start).match(/(?: {1,2}|\t)$/)?.[0].length ?? 0;
  if (removable === 0) return;
  const value = `${document.value.slice(0, start - removable)}${document.value.slice(start)}`;
  onChange({ ...document, value });
  queueMicrotask(() => input.setSelectionRange(start - removable, start - removable));
}
