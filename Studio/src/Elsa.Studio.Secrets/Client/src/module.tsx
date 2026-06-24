import React from "react";
import { KeyRound } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SecretsPage } from "./SecretsPage";
import { SecretPickerEditor } from "./SecretPickerEditor";
import "./styles.css";

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;

  api.featureAreas.add({
    id: "secrets",
    title: "Secrets",
    description: "Manage named secrets and bind workflow inputs to secret references.",
    navGroup: "Workspace",
    ownedPaths: ["/secrets"],
    required: false,
    defaultEnabled: true,
    order: 45,
    nav: {
      title: "Secrets",
      path: "/secrets",
      iconColor: "#64748b"
    },
    routes: [
      {
        id: "secrets",
        path: "/secrets",
        label: "Secrets",
        component: () => <SecretsPage context={api.backend} />
      }
    ]
  });

  api.propertyEditors.add({
    id: "secret-picker",
    order: 30,
    supports: descriptor => descriptor.uiHint === "secret-picker" || descriptor.defaultSyntax === "Secret",
    component: props => <SecretPickerEditor {...props} endpointContext={moduleApi.backend} />
  });
}

export function SecretsIcon() {
  return <KeyRound aria-hidden="true" size={16} />;
}
