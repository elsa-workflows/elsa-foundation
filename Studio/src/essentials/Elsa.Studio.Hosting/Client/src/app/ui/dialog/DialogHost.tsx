import { StudioField } from "../forms/Field";
import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { StudioDialog } from "@elsa-workflows/studio-ui";
import type { StudioDialogRequest } from "../../../sdk";
import { dialogController } from "../../dialogs";

/**
 * Single host-rendered surface for `api.dialogs.confirm/prompt/alert`. Subscribes to the
 * shared dialog controller and renders the head request with Radix (focus trap, Esc,
 * scroll-lock, ARIA), restoring the imperative caller's opener and using Studio design tokens.
 */
export function DialogHost() {
  const request = useSyncExternalStore(dialogController.subscribe, dialogController.getCurrent, dialogController.getCurrent);
  const opener = useRef<HTMLElement | null>(null);
  const wasOpen = useRef(false);
  if (request && !wasOpen.current && typeof document !== "undefined") {
    const focused = document.activeElement;
    // Imperative dialogs have no Radix Trigger. Preserve the original opener across a queued dialog
    // or a replacement that arrives while the previous content is still animating closed.
    if (!(focused instanceof HTMLElement) || !focused.closest(".studio-dialog")) {
      opener.current = focused instanceof HTMLElement && focused !== document.body && focused !== document.documentElement ? focused : null;
    }
  }
  wasOpen.current = request != null;

  // If this surface tears down while dialogs are pending, settle them as cancelled so no caller hangs.
  useEffect(() => () => dialogController.cancelAll(), []);

  function close() {
    // Esc / overlay click / dismiss — resolve as cancel for the active request.
    if (request) {
      dialogController.respond(request.id, request.kind === "prompt" ? null : false);
    }
  }

  return (
    <StudioDialog
      open={request != null}
      onClose={close}
      title={request?.title || (request ? defaultTitleForKind(request.kind) : "")}
      hideTitle={!request?.title}
      description={request?.message}
      hideCloseButton
      onCloseAutoFocus={event => {
        event.preventDefault();
        if (dialogController.getCurrent()) return;
        const target = opener.current;
        opener.current = null;
        const focused = document.activeElement;
        const needsRestoration = !focused || focused === document.body || focused === document.documentElement || focused.closest(".studio-dialog");
        if (needsRestoration && target?.isConnected && !target.matches(":disabled, [aria-disabled='true']")) target.focus();
      }}
    >
      {request ? <DialogBody key={request.id} request={request} /> : null}
    </StudioDialog>
  );
}

function DialogBody({ request }: { request: StudioDialogRequest }) {
  const [value, setValue] = useState(request.defaultValue ?? "");
  const isPrompt = request.kind === "prompt";
  const isAlert = request.kind === "alert";

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
      {isPrompt ? (
        <StudioField label={request.title ?? request.message}><input
          // eslint-disable-next-line jsx-a11y/no-autofocus
          autoFocus
          className="studio-dialog-input"
          value={value}
          placeholder={request.placeholder}
          aria-label={request.title ?? request.message}
          onChange={event => setValue(event.target.value)}
        /></StudioField>
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
