import { useCallback, useEffect, useRef } from "react";
import type {
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext
} from "@elsa-workflows/studio-sdk";

export function ReportedPropertyEditor({
  editor,
  descriptor,
  value,
  disabled,
  context,
  onChange,
  validationInstanceKey,
  onValidationChange,
  onDisclosureFallbackChange
}: {
  editor: StudioActivityPropertyEditorContribution;
  descriptor: StudioActivityInputDescriptor;
  value: unknown;
  disabled: boolean;
  context: StudioActivityPropertyEditorContext;
  onChange(value: unknown): void;
  validationInstanceKey: string;
  onValidationChange?(instanceKey: string, hasError: boolean): void;
  onDisclosureFallbackChange?(instanceKey: string, shouldForceOpen: boolean): void;
}) {
  const active = useRef(true);
  const latestReporter = useRef(onValidationChange);
  latestReporter.current = onValidationChange;
  const report = useCallback((hasError: boolean) => {
    if (active.current) latestReporter.current?.(validationInstanceKey, hasError);
  }, [validationInstanceKey]);

  useEffect(() => {
    active.current = true;
    return () => {
      active.current = false;
      latestReporter.current?.(validationInstanceKey, false);
    };
  }, [validationInstanceKey]);

  useEffect(() => {
    onDisclosureFallbackChange?.(validationInstanceKey, editor.supportsValidationReporting !== true);
    return () => onDisclosureFallbackChange?.(validationInstanceKey, false);
  }, [editor.supportsValidationReporting, onDisclosureFallbackChange, validationInstanceKey]);

  const Editor = editor.component;
  return (
    <Editor
      descriptor={descriptor}
      value={value}
      disabled={disabled}
      context={context}
      onChange={onChange}
      onValidationChange={editor.supportsValidationReporting === true ? report : undefined}
    />
  );
}
