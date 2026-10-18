import React, { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ConnectionsPage } from "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";

const descriptors = {
  types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [
    { name: "baseUrl", displayName: "Base URL", editorHint: "url" }
  ] }],
  schemes: [{ name: "bearer", displayName: "Bearer", fields: [
    { name: "token", displayName: "Token", sensitive: true, editorHint: "password", required: true }
  ], hasInteractiveAuthorization: false }]
};

function Fixture() {
  const [payload, setPayload] = useState<unknown>();
  const context = useMemo<StudioEndpointContext>(() => ({
    baseUrl: "http://fixture.example",
    http: {
      getJson: async <T,>(url: string) => (url.endsWith("/descriptors") ? descriptors : { items: [] }) as T,
      postJson: async <T,>(url: string, body: unknown) => {
        if (url === "/secrets/picker") {
          const search = (body as { search?: string }).search?.toLowerCase() ?? "";
          return { items: [
            { id: "customer-token", name: "customer-api", displayName: "Customer API token", typeName: "text", storeName: "encrypted", status: "active", scope: "demo", expiresAt: null },
            { id: "other-token", name: "other-api", displayName: "Other API token", typeName: "text", storeName: "encrypted", status: "active", expiresAt: null }
          ].filter(item => `${item.name} ${item.displayName}`.toLowerCase().includes(search)), canCreateInline: false } as T;
        }
        if (url === "/connections") {
          setPayload(body);
          return { ...(body as object), tenantId: "default", displayName: "Customer API", status: "Ready", secretFieldNames: ["token"], tags: [] } as T;
        }
        throw new Error(`Unexpected fixture route ${url}`);
      },
      requestJson: async () => { throw new Error("No raw request expected"); },
      putJson: async () => { throw new Error("No update expected"); },
      deleteJson: async () => { throw new Error("No delete expected"); }
    }
  }), []);
  return <ThemeProvider><main>
    <ConnectionsPage context={context} dialogs={{ confirm: async () => true, prompt: async () => null, alert: async () => {} }} />
    {payload ? <output data-testid="last-connection-payload">{JSON.stringify(payload)}</output> : null}
    <button type="button">Outside control</button>
  </main></ThemeProvider>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);
