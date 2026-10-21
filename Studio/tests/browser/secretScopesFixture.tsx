import React, { useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { SecretsPage } from "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage";
import type { CreateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/secretTypes";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/styles.css";

const scenario = new URLSearchParams(window.location.search).get("scenario") ?? "default";

const tallDescription = scenario === "tall-error"
  ? Array.from({ length: 16 }, () => "This explanatory text is intentionally long so the create dialog has enough content to test its own scrolling area.").join(" ")
  : undefined;

const suggestedScopes = [
  { name: "Integration:CRM", displayName: "CRM integration", description: "Credentials used by the CRM integration.", source: "CRM Connector" },
  { name: "Integration.Legacy-CRM", displayName: "Legacy CRM integration", description: "Existing CRM credential scope.", source: "Legacy CRM Provider" },
  { name: "Integration:Billing", displayName: "Billing integration", description: "Credentials used by the billing integration.", source: "Commerce Connector" },
  { name: "Integration:Invoicing", displayName: "Invoicing integration", description: "Credentials used by the invoicing integration.", source: "Commerce Connector" },
  { name: "Integration:Shipping", displayName: "Shipping integration", description: "Credentials used by shipping services.", source: "Logistics Connector" },
  { name: "Integration:Returns", displayName: "Returns integration", description: "Credentials used by returns services.", source: "Logistics Connector" },
  { name: "Team:Billing", displayName: "Billing team", description: "Billing integration credentials.", source: "Billing Module" },
  { name: "Team:Support", displayName: "Support team", description: "Support integration credentials.", source: "Support Module" }
];

const descriptors: SecretDescriptorsResponse = {
  types: [{ name: "text", displayName: "Text", description: tallDescription, editorHint: "text", supportedStoreNames: scenario === "configuration" ? ["configuration", "encrypted"] : ["encrypted"] }],
  stores: [
    ...(scenario === "configuration" ? [{ name: "configuration", displayName: "Configuration", isReadOnly: false }] : []),
    { name: "encrypted", displayName: "Encrypted", description: tallDescription, isReadOnly: false }
  ],
  scopes: suggestedScopes
};

const initialEditorSecret: SecretMetadata = {
  id: "fixture-integration-secret",
  name: "billing.integration-key",
  displayName: "Billing integration credentials",
  description: "Credentials used by the browser fixture integration.",
  typeName: "text",
  storeName: "encrypted",
  scope: "Integration:CRM",
  tags: ["browser-fixture", "metadata-only"],
  status: "Active",
  currentVersion: 2,
  createdAt: "2026-10-01T12:00:00Z",
  updatedAt: "2026-10-17T12:00:00Z",
  expiresAt: null
};

function Fixture() {
  const [unexpectedReadCount, setUnexpectedReadCount] = useState(0);
  const [createdMetadata, setCreatedMetadata] = useState<Pick<SecretMetadata, "name" | "displayName" | "typeName" | "storeName" | "scope" | "tags" | "status"> | null>(null);
  const [updatedMetadata, setUpdatedMetadata] = useState<Pick<SecretMetadata, "name" | "displayName" | "description" | "typeName" | "storeName" | "scope" | "status"> | null>(null);
  const editorSecret = useRef<SecretMetadata>({ ...initialEditorSecret });
  const updateRequestCount = useRef(0);
  const descriptorRequestCount = useRef(0);
  const context = useMemo<StudioEndpointContext>(() => ({
    baseUrl: "http://fixture.example",
    http: {
      getJson: async <T,>(url: string) => {
        if (url === "/secrets/descriptors") {
          descriptorRequestCount.current += 1;
          const requestNumber = descriptorRequestCount.current;

          if (scenario === "focused-retry" && requestNumber === 2) {
            await new Promise<void>(resolve => window.addEventListener("fixture-scope-failure", () => resolve(), { once: true }));
            throw new Error("Fixture scope catalog refresh failed.");
          }
          if (scenario === "retry-removed" && requestNumber === 2) {
            throw new Error("Fixture scope catalog refresh failed.");
          }
          if (scenario === "loading" && requestNumber === 2) {
            await new Promise(resolve => window.setTimeout(resolve, 800));
          }

          const scopes = scenario === "retry-removed" && requestNumber >= 3 ? [] : suggestedScopes;
          return { ...descriptors, scopes } as T;
        }
        if (url.startsWith("/secrets?")) {
          const items = scenario.startsWith("editor") ? [editorSecret.current] : [];
          return { items, totalCount: items.length } as T;
        }
        setUnexpectedReadCount(count => count + 1);
        throw new Error(`Unexpected fixture GET ${url}`);
      },
      postJson: async <T,>(url: string, body: unknown) => {
        if (url !== "/secrets") throw new Error(`Unexpected fixture POST ${url}`);
        if (scenario === "tall-error") {
          const details = Array.from({ length: 24 }, () => "The test service rejected this secret after validating its metadata and selected storage configuration.").join(" ");
          throw new Error(`Fixture secret creation failed. ${details}`);
        }
        const request = body as CreateSecretRequest;
        const metadata: SecretMetadata = {
          id: `fixture-${request.name}`,
          name: request.name,
          displayName: request.displayName ?? request.name,
          typeName: request.typeName,
          storeName: request.storeName,
          scope: request.scope ?? null,
          tags: request.tags ?? [],
          status: "Active",
          createdAt: "2026-10-18T12:00:00Z"
        };

        // Keep the fixture response and DOM strictly metadata-only. In particular, never echo
        // request.value back into the page or into the synthetic secret record.
        setCreatedMetadata({
          name: metadata.name,
          displayName: metadata.displayName,
          typeName: metadata.typeName,
          storeName: metadata.storeName,
          scope: metadata.scope,
          tags: metadata.tags,
          status: metadata.status
        });
        return metadata as T;
      },
      putJson: async <T,>(url: string, body: unknown) => {
        if (url !== `/secrets/${encodeURIComponent(initialEditorSecret.name)}`) {
          throw new Error(`Unexpected fixture PUT ${url}`);
        }
        updateRequestCount.current += 1;
        if (scenario === "editor-error" && updateRequestCount.current === 1) {
          throw new Error("Fixture metadata update failed.");
        }

        const request = body as { displayName: string; description?: string };
        editorSecret.current = {
          ...editorSecret.current,
          displayName: request.displayName,
          description: request.description,
          updatedAt: "2026-10-18T12:00:00Z"
        };
        setUpdatedMetadata({
          name: editorSecret.current.name,
          displayName: editorSecret.current.displayName,
          description: editorSecret.current.description,
          typeName: editorSecret.current.typeName,
          storeName: editorSecret.current.storeName,
          scope: editorSecret.current.scope,
          status: editorSecret.current.status
        });
        return editorSecret.current as T;
      },
      requestJson: async () => { throw new Error("No raw request expected in this fixture"); },
      deleteJson: async () => { throw new Error("No delete expected in this fixture"); },
      postForm: async () => { throw new Error("No form request expected in this fixture"); }
    }
  }), []);

  return (
    <ThemeProvider>
      <SecretsPage context={context} dialogs={{ confirm: async () => true, prompt: async () => null, alert: async () => undefined }} />
      <output data-testid="unexpected-secret-reads">{unexpectedReadCount}</output>
      {createdMetadata ? <output data-testid="created-secret-metadata">{JSON.stringify(createdMetadata)}</output> : null}
      {updatedMetadata ? <output data-testid="updated-secret-metadata">{JSON.stringify(updatedMetadata)}</output> : null}
    </ThemeProvider>
  );
}

createRoot(document.getElementById("root")!).render(<Fixture />);
