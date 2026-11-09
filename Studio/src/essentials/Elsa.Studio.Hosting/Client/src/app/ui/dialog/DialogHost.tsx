import { useEffect, useState, useSyncExternalStore } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import type { StudioDialogRequest } from "../../../sdk";
import { dialogController } from "../../dialogs";

/**
 * Single host-rendered surface for `api.dialogs.confirm/prompt/alert`. Subscribes to the
 * shared dialog controller and renders the head request with Radix (focus trap, Esc,
 * scroll-lock, ARIA, focus restore), styled with Studio design tokens.
 */
export function DialogHost() {
  const request = useSyncExternalStore(dialogController.subscribe, dialogController.getCurrent, dialogController.getCurrent);

  // If this surface tears down while dialogs are pending, settle them as cancelled so no caller hangs.
  useEffect(() => () => dialogController.cancelAll(), []);

  function handleOpenChange(next: boolean) {
    // Esc / overlay click / dismiss — resolve as cancel for the active request.
    if (!next && request) {
      dialogController.respond(request.id, request.kind === "prompt" ? null : false);
    }
  }

  return (
    <Dialog.Root open={request != null} onOpenChange={handleOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="studio-dialog-overlay" />
        <Dialog.Content className="studio-dialog" data-tone={request?.tone ?? "default"}>
          {request ? <DialogBody key={request.id} request={request} /> : null}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function DialogBody({ request }: { request: StudioDialogRequest }) {
  const [value, setValue] = useState(request.defaultValue ?? "");
  const isPrompt = request.kind === "prompt";
  const isAlert = request.kind === "alert";
  const hasTitle = Boolean(request.title);

  function confirm() {
    dialogController.respond(request.id, isPrompt ? value : true);
  }

  function cancel() {
    dialogController.respond(request.id, isPrompt ? null : false);
  }

  return (
    <form
      className="studio-dialog-body"
      onSubmit={event => {
        event.preventDefault();
        confirm();
      }}
    >
      {/* A Title is required for accessibility; when no explicit title is supplied the message
          is the visible heading (matching native confirm) and the title is screen-reader only. */}
      {hasTitle ? (
        <Dialog.Title className="studio-dialog-title">{request.title}</Dialog.Title>
      ) : (
        <Dialog.Title className="studio-dialog-sr-only">{defaultTitleForKind(request.kind)}</Dialog.Title>
      )}
      <Dialog.Description className="studio-dialog-message">{request.message}</Dialog.Description>

      {isPrompt ? (
        <input
          // eslint-disable-next-line jsx-a11y/no-autofocus
          autoFocus
          className="studio-dialog-input"
          value={value}
          placeholder={request.placeholder}
          aria-label={request.title ?? request.message}
          onChange={event => setValue(event.target.value)}
        />
      ) : null}

      <div className="studio-dialog-actions">
        {!isAlert ? (
          <button type="button" className="studio-button" onClick={cancel}>
            {request.cancelLabel ?? "Cancel"}
          </button>
        ) : null}
        <button type="submit" className="studio-button studio-dialog-confirm" data-tone={request.tone ?? "default"}>
          {request.confirmLabel ?? (isAlert ? "OK" : "Confirm")}
        </button>
      </div>
    </form>
  );
}

function defaultTitleForKind(kind: StudioDialogRequest["kind"]) {
  if (kind === "prompt") {
    return "Enter a value";
  }

  if (kind === "alert") {
    return "Notice";
  }

  return "Confirm";
}
