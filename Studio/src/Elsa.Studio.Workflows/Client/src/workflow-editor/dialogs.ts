import type { StudioDialogApi } from "@elsa-workflows/studio-sdk";

// Host-provided dialog service, captured at registration so imperative handlers can await it
// without prop-drilling. Assigned before any route component renders.
let dialogs: StudioDialogApi;

export function setDialogs(api: StudioDialogApi) {
  dialogs = api;
}

export function getDialogs(): StudioDialogApi {
  return dialogs;
}
