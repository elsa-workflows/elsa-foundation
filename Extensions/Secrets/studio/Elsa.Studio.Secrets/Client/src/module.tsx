import React from "react";
import { KeyRound } from "lucide-react";
import { useStudioRouteLocation, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SecretEditorPage } from "./SecretEditorPage";
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
      icon: KeyRound,
      requiresBackend: [{id:"elsa.api.secrets",contractVersion:"1"}],
      title: "Secrets",
      path: "/security/secrets",
      iconColor: "#64748b",
      placement: "more",
      order: 30
    },
    routes: [
      ...["/security/secrets", "/secrets"].flatMap(prefix => [
        { id: `${prefix}.create`, path: `${prefix}/new`, label: "Create secret", requiresBackend: [{ id: "elsa.api.secrets", contractVersion: "1" }], workspace: { resourceKey: () => "secret-editor:new" }, component: ({ navigate }: { navigate(path: string): void }) => <SecretEditorPage key={api.backendSession?.key ?? api.backend.baseUrl} api={api} navigate={navigate} /> },
        { id: `${prefix}.edit`, path: `${prefix}/:name/edit`, label: "Edit secret", requiresBackend: [{ id: "elsa.api.secrets", contractVersion: "1" }], workspace: { resourceKey: (location: URL) => { try { return `secret:${decodeURIComponent(location.pathname.split("/").at(-2) ?? "").toLowerCase()}`; } catch { return null; } } }, component: ({ navigate }: { navigate(path: string): void }) => <SecretEditRoute api={api} navigate={navigate} /> }
      ]),
      {
        id: "secrets", requiresBackend: [{id:"elsa.api.secrets",contractVersion:"1"}],
        path: "/security/secrets",
        label: "Secrets",
        component: ({ navigate }) => <SecretsPage context={api.backend} dialogs={api.dialogs} navigate={navigate} notifications={api.notifications} />
      },
      {
        id: "secrets-legacy",
        path: "/secrets",
        label: "Secrets",
        component: ({ navigate }) => <SecretsPage context={api.backend} dialogs={api.dialogs} navigate={navigate} notifications={api.notifications} />
      }
    ]
  });

  api.expressionEditors.add({
    id: "elsa.secret-reference-editor", requiresBackend: [{id:"elsa.api.secrets",contractVersion:"1"}],
    order: 100,
    supports: context => context.syntax === "Secret",
    surfaces: {
      inline: props => <SecretPickerEditor {...props} endpointContext={api.backend} />
    },
    createDefaultValue: () => null,
  });
}

function SecretEditRoute({ api, navigate }: { api: ElsaStudioModuleApi; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  let name: string;
  try { name = decodeURIComponent(location.pathname.split("/").at(-2) ?? ""); } catch { return <p role="alert">Invalid secret address.</p>; }
  return <SecretEditorPage key={JSON.stringify([api.backendSession?.key ?? api.backend.baseUrl, name.toLowerCase()])} api={api} name={name} navigate={navigate} />;
}

export function SecretsIcon() {
  return <KeyRound aria-hidden="true" size={16} />;
}
