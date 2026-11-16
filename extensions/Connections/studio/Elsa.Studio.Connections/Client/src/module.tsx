import React from "react";
import { Cable } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ConnectionsPage } from "./ConnectionsPage";
import { ConnectionPickerEditor } from "./ConnectionPickerEditor";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({
    id: "connections",
    title: "Connections",
    description: "Manage service connections and bind workflow inputs to connection references.",
    navGroup: "Security",
    ownedPaths: ["/security/connections"],
    required: false,
    defaultEnabled: true,
    order: 46,
    nav: {
      icon: Cable,
      requiresBackend: [{id:"elsa.api.connections",contractVersion:"1"}],
      title: "Connections",
      path: "/security/connections",
      iconColor: "#64748b",
      placement: "integrations",
      order: 10
    },
    routes: [
      {
        id: "connections", requiresBackend: [{id:"elsa.api.connections",contractVersion:"1"}],
        path: "/security/connections",
        label: "Connections",
        component: () => <ConnectionsPage context={api.backend} dialogs={api.dialogs} notifications={api.notifications} />
      }
    ]
  });

  api.expressionEditors.add({
    id: "elsa.connection-reference-editor", requiresBackend: [{id:"elsa.api.connections",contractVersion:"1"}],
    presentsField: true,
    order: 100,
    supports: context => context.syntax === "Connection" || context.descriptor.uiHint === "connection-picker",
    surfaces: {
      inline: props => <ConnectionPickerEditor {...props} endpointContext={api.backend} />
    },
    createDefaultValue: () => null
  });
}

export function ConnectionsIcon() {
  return <Cable aria-hidden="true" size={16} />;
}
