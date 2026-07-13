import React from "react";
import { KeyRound } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SecretsPage } from "./SecretsPage";
import { SecretPickerEditor } from "./SecretPickerEditor";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({
    id: "secrets",
    title: "Secrets",
    description: "Manage named secrets and bind workflow inputs to secret references.",
    navGroup: "Security",
    ownedPaths: ["/security/secrets", "/secrets"],
    required: false,
    defaultEnabled: true,
    order: 45,
    nav: {
      title: "Secrets",
      path: "/security/secrets",
      iconColor: "#64748b"
    },
    routes: [
      {
        id: "secrets",
        path: "/security/secrets",
        label: "Secrets",
        component: () => <SecretsPage context={api.backend} dialogs={api.dialogs} />
      },
      {
        id: "secrets-legacy",
        path: "/secrets",
        label: "Secrets",
        component: () => <SecretsPage context={api.backend} dialogs={api.dialogs} />
      }
    ]
  });

  api.expressionEditors.add({
    id: "elsa.secret-reference-editor",
    order: 100,
    supports: context => context.syntax === "Secret",
    surfaces: {
      inline: props => <SecretPickerEditor {...props} endpointContext={api.backend} />
    },
    createDefaultValue: () => null,
  });
}

export function SecretsIcon() {
  return <KeyRound aria-hidden="true" size={16} />;
}
