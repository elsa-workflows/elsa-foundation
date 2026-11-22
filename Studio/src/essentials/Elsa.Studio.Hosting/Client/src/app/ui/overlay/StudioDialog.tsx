import React, { useRef } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import { StudioButton } from "../forms/Button";

export interface StudioDialogProps {
  open: boolean;
  onClose(): void;
  title: React.ReactNode;
  description?: React.ReactNode;
  children?: React.ReactNode;
  footer?: React.ReactNode;
  className?: string;
  onSubmit?: React.FormEventHandler<HTMLFormElement>;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  dismissDisabled?: boolean;
  dismissOnOutsideClick?: boolean;
  /** Retain a dialog name when a legacy caller only supplies a visible message. */
  hideTitle?: boolean;
  hideCloseButton?: boolean;
  onCloseAutoFocus?: (event: Event) => void;
}

export function StudioDialog({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  className,
  onSubmit,
  initialFocusRef,
  dismissDisabled = false,
  dismissOnOutsideClick = true,
  hideTitle = false,
  hideCloseButton = false,
  onCloseAutoFocus
}: StudioDialogProps) {
  const opener = useRef<HTMLElement | null>(null);
  const hasDescription = description != null && description !== false;
  const content = <>
    <Dialog.Title className={hideTitle ? "studio-dialog-sr-only" : "studio-dialog-title"}>{title}</Dialog.Title>
    {!hideCloseButton ? (
      <Dialog.Close asChild>
        <StudioButton className="studio-dialog-close" variant="ghost" aria-label="Close dialog" disabled={dismissDisabled}>
          <X size={16} aria-hidden="true" />
        </StudioButton>
      </Dialog.Close>
    ) : null}
    {hasDescription ? <Dialog.Description className="studio-dialog-message">{description}</Dialog.Description> : null}
    {children}
    {footer}
  </>;

  return (
    <Dialog.Root open={open} onOpenChange={next => { if (!next && !dismissDisabled) onClose(); }}>
      <Dialog.Portal>
        <Dialog.Overlay className="studio-dialog-overlay" />
        <Dialog.Content
          asChild
          className={className ? `studio-dialog ${className}` : "studio-dialog"}
          aria-modal="true"
          data-has-close={!hideCloseButton || undefined}
          {...(!hasDescription ? { "aria-describedby": undefined } : {})}
          onEscapeKeyDown={event => { if (dismissDisabled) event.preventDefault(); }}
          onPointerDownOutside={event => { if (dismissDisabled || !dismissOnOutsideClick) event.preventDefault(); }}
          onOpenAutoFocus={event => {
            opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
            const initial = initialFocusRef?.current;
            if (initial && !initial.matches(":disabled")) {
              initial.focus();
              if (document.activeElement === initial) event.preventDefault();
            }
          }}
          onCloseAutoFocus={onCloseAutoFocus ?? (event => {
            event.preventDefault();
            const target = opener.current;
            opener.current = null;
            if (document.activeElement === document.body && target?.isConnected) target.focus();
          })}
        >
          {onSubmit ? <form onSubmit={onSubmit}>{content}</form> : <div>{content}</div>}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}