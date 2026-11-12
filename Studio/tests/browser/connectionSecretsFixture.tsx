import React, { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ConnectionsPage } from "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";

const fixtureQuery = new URLSearchParams(window.location.search);
const scenario = fixtureQuery.get("scenario") ?? "default";
const mode = fixtureQuery.get("mode") === "dark" ? "dark" : "light";
const requestedExpiry = Number(fixtureQuery.get("expiresAt"));
const expiringSecret = Number.isFinite(requestedExpiry) && requestedExpiry > 0
  ? new Date(requestedExpiry).toISOString()
  : null;
writeAppearancePreferences({ themeId: "classic", mode });

const extraSettings = scenario === "tall-error"
  ? Array.from({ length: 12 }, (_, index) => ({
    name: `extra-${index + 1}`,
    displayName: `Extra setting ${index + 1}`,
    editorHint: "text"
  }))
  : [];

const descriptors = {
  types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [
    { name: "baseUrl", displayName: "Base URL", editorHint: "url" },
    ...extraSettings
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
          if (scenario === "picker-503") {
            throw Object.assign(new Error("Fixture provider diagnostic"), { status: 503 });
          }
          const search = (body as { search?: string }).search?.toLowerCase() ?? "";
          const items = scenario === "expiring-secret"
            ? [{ id: "customer-token", name: "customer-api", displayName: "Customer API token", typeName: "text", storeName: "encrypted", status: "active", scope: "demo", expiresAt: expiringSecret }]
            : [
                { id: "customer-token", name: "customer-api", displayName: "Customer API token", typeName: "text", storeName: "encrypted", status: "active", scope: "demo", expiresAt: null },
                { id: "other-token", name: "other-api", displayName: "Other API token", typeName: "text", storeName: "encrypted", status: "active", scope: "demo", expiresAt: null }
              ];
          return { items: items.filter(item => `${item.name} ${item.displayName}`.toLowerCase().includes(search)), canCreateInline: false } as T;
        }
        if (url === "/connections") {
          if (scenario === "tall-error") {
            const details = Array.from({ length: 24 }, () => "The test service rejected this connection after validating its configuration and credentials.").join(" ");
            throw new Error(`Fixture connection create failed. ${details}`);
          }
          if (scenario === "create-error") throw new Error("Fixture connection create failed.");
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
