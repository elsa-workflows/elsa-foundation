import type { ReactNode } from "react";
import { StudioDialog, StudioDialogFooter } from "@elsa-workflows/studio-ui";

export function ActivityDefinitionManagementDialogShell({ title, submitting, onClose, children }: {
  title: string;
  submitting: boolean;
  onClose(): void;
  children: ReactNode;
}) {
  return <StudioDialog open title={title} className="ad-management-dialog" dismissDisabled={submitting} onClose={onClose}>
    {children}
  </StudioDialog>;
}

export function ActivityDefinitionManagementDialogActions({
  submitting,
  submitLabel,
  canSubmit,
  onClose,
  onSubmit
}: {
  submitting: boolean;
  submitLabel: string;
  canSubmit: boolean;
  onClose(): void;
  onSubmit(): Promise<void>;
}) {
  return <StudioDialogFooter confirmType="button" onCancel={onClose} cancelDisabled={submitting}
    confirmLabel={submitting ? "Applying atomically…" : submitLabel} confirmDisabled={!canSubmit} onConfirm={() => void onSubmit()} />;
}
