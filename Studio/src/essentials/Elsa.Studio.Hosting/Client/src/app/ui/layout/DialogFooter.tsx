import React from "react";
import { StudioButton, type StudioButtonVariant } from "../forms/Button";
import "./DialogFooter.css";

export interface StudioDialogFooterProps extends React.ComponentPropsWithoutRef<"footer"> {
  /** Optional control on the leading edge (for example Reload). */
  leading?: React.ReactNode;
  /** Extra actions rendered before Cancel, still using caller-supplied StudioButtons. */
  extra?: React.ReactNode;
  cancelLabel?: React.ReactNode;
  onCancel?: React.MouseEventHandler<HTMLButtonElement>;
  cancelDisabled?: boolean;
  cancelProps?: React.ComponentPropsWithoutRef<typeof StudioButton>;
  confirmLabel?: React.ReactNode;
  onConfirm?: React.MouseEventHandler<HTMLButtonElement>;
  confirmDisabled?: boolean;
  confirmType?: "button" | "submit";
  confirmVariant?: StudioButtonVariant;
  confirmProps?: React.ComponentPropsWithoutRef<typeof StudioButton>;
}

/**
 * Shared dialog action row. Cancel/Close is always the default StudioButton;
 * the confirming action is `variant="primary"` unless the caller overrides it.
 */
export function StudioDialogFooter({
  leading,
  extra,
  cancelLabel = "Cancel",
  onCancel,
  cancelDisabled,
  cancelProps,
  confirmLabel,
  onConfirm,
  confirmDisabled,
  confirmType = "submit",
  confirmVariant = "primary",
  confirmProps,
  className,
  children,
  ...props
}: StudioDialogFooterProps) {
  return (
    <footer className={["studio-dialog-footer", className].filter(Boolean).join(" ")} {...props}>
      {leading ? <div className="studio-dialog-footer-leading">{leading}</div> : null}
      <div className="studio-dialog-footer-actions">
        {extra}
        {children}
        <StudioButton disabled={cancelDisabled} onClick={onCancel} {...cancelProps}>{cancelLabel}</StudioButton>
        {confirmLabel != null && confirmLabel !== false ? (
          <StudioButton variant={confirmVariant} type={confirmType} disabled={confirmDisabled} onClick={onConfirm} {...confirmProps}>
            {confirmLabel}
          </StudioButton>
        ) : null}
      </div>
    </footer>
  );
}
