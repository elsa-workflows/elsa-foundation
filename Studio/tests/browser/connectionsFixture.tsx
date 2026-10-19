import React from "react";
import { createEndpointContext, type StudioDialogApi } from "@elsa-workflows/studio-sdk";
import { ConnectionsPage } from "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage";
import "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";

const context = createEndpointContext(new URLSearchParams(window.location.search).get("backend") || window.location.origin);
// Destructive actions remain inert in this presentation fixture. Lifecycle tests exercise the API separately.
const dialogs: StudioDialogApi = { confirm: async () => false, alert: async () => {}, prompt: async () => null };

export function ConnectionsFixture() {
  return <main><ConnectionsPage context={context} dialogs={dialogs} /></main>;
}
