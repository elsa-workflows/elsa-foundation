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
    ownedPaths: ["/security/connections", "/connections"],
    required: false,
    defaultEnabled: true,
    order: 46,
    nav: {
      title: "Connections",
      path: "/security/connections",
      iconColor: "#64748b",
      placement: "more",
      order: 31
    },
    routes: [
      {
        id: "connections",
        path: "/security/connections",
        label: "Connections",
        component: () => <ConnectionsPage context={api.backend} dialogs={api.dialogs} />
      },
      {
        id: "connections-legacy",
        path: "/connections",
        label: "Connections",
        component: () => <ConnectionsPage context={api.backend} dialogs={api.dialogs} />
      }
    ]
  });

  api.expressionEditors.add({
    id: "elsa.connection-reference-editor",
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
