import React, { useState } from "react";
import { ActivityPropertiesPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { createEndpointContext, type StudioActivityDescriptor, type StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { ConnectionPickerEditor } from "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionPickerEditor";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const endpointContext = createEndpointContext(window.location.origin);

const descriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.SendHttpRequest",
  displayName: "Send HTTP Request",
  inputs: [
    { name: "Url", referenceKey: "Url", displayName: "URL", typeName: "System.Uri", isWrapped: true, tier: "essential", isRequired: true },
    {
      name: "Method", referenceKey: "Method", displayName: "Method", typeName: "System.String",
      isWrapped: true, defaultSyntax: "Literal", defaultValue: "GET", uiHint: "dropdown",
      uiSpecifications: { options: ["GET", "POST", "PUT", "DELETE", "HEAD"].map(value => ({ label: value, value })) },
      tier: "essential"
    },
    { name: "Content", referenceKey: "Content", displayName: "Content", typeName: "System.String", isWrapped: true, uiHint: "multiline", defaultSyntax: "Literal", tier: "essential" },
    {
      name: "Connection", referenceKey: "Connection", displayName: "Connection", typeName: "ConnectionReference",
      isWrapped: true, defaultSyntax: "Connection", uiHint: "connection-picker", connectionType: "http",
      tier: "essential", isRequired: false
    },
    {
      name: "ContentType", referenceKey: "ContentType", displayName: "Content type", typeName: "System.String",
      isWrapped: true, defaultSyntax: "Literal", defaultValue: "text/plain", uiHint: "dropdown",
      uiSpecifications: { options: ["text/plain", "application/json"].map(value => ({ label: value, value })) },
      tier: "optional"
    },
    {
      name: "RequestHeaders", referenceKey: "RequestHeaders", displayName: "Request headers", typeName: "System.String",
      isWrapped: true, defaultSyntax: "JavaScript", collectionKind: "Dictionary", tier: "optional"
    },
    { name: "ExpectedStatusCodes", referenceKey: "ExpectedStatusCodes", displayName: "Expected status codes", typeName: "System.Int32", isWrapped: true, collectionKind: "List", tier: "optional" },
    { name: "Timeout", referenceKey: "Timeout", displayName: "Timeout", typeName: "System.TimeSpan", isWrapped: true, defaultSyntax: "Literal", tier: "optional" }
  ],
  outputs: [],
  ports: []
};

const expressionEditors: StudioExpressionEditorContribution[] = [
  {
    id: "browser.http-connection-picker",
    supports: context => context.syntax === "Connection",
    surfaces: { inline: props => <ConnectionPickerEditor {...props} endpointContext={endpointContext} /> },
    createDefaultValue: () => null
  },
  {
    id: "browser.http-request-header-diagnostics",
    supports: context => context.descriptor.referenceKey === "RequestHeaders" && context.syntax === "JavaScript",
    surfaces: {},
    diagnostics: (_context, value) => String(value) === "invalid"
      ? [{ severity: "error", code: "HTTP_HEADERS_INVALID", message: "Request headers expression is invalid." }]
      : []
  }
];

const expressionDescriptors = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Connection", displayName: "Connection", editingMode: "reference" }
];

export function HttpProgressiveDisclosureFixture() {
  const seededDefault = new URLSearchParams(window.location.search).get("seed") === "1";
  const [activity, setActivity] = useState<ActivityNode>({
    nodeId: "http-request-browser-node",
    activityVersionId: "http-request-browser-v1",
    inputs: [],
    outputs: [],
    Url: { typeName: "System.Uri", expression: { type: "Literal", value: "https://api.example/orders" } },
    Method: { typeName: "System.String", expression: { type: "Literal", value: "GET" } },
    Content: { typeName: "System.String", expression: { type: "Literal", value: "existing request body" } },
    Connection: { typeName: "ConnectionReference", expression: { type: "Connection", value: null } },
    ...(seededDefault ? {
      ContentType: {
        typeName: "System.String",
        expression: { type: "Literal", value: "text/plain" },
        conversion: { mode: "profile", profile: { id: "body-conversion", version: "1" } },
        argumentExtras: { source: "foreign-template", future: { preserve: true } }
      }
    } : {})
  });

  return (
    <main style={{ minHeight: "100vh", padding: 16, background: "var(--studio-app-bg)", color: "var(--studio-text)" }}>
      <aside
        className="wf-inspector"
        aria-label="Activity inspector"
        style={{ width: 480, maxWidth: "100%", boxSizing: "border-box", height: "calc(100vh - 32px)", overflowY: "auto", marginLeft: "auto", border: "1px solid var(--studio-border)", borderRadius: 8, background: "var(--studio-surface)", color: "var(--studio-text)", padding: 16 }}
      >
        <h1 style={{ marginTop: 0, fontSize: 18 }}>Send HTTP Request</h1>
        <ActivityPropertiesPanel
          activity={activity}
          descriptor={descriptor}
          editors={builtInPropertyEditors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={setActivity}
        />
        <output data-testid="authored-state" style={{ display: "none" }}>{JSON.stringify(activity)}</output>
      </aside>
    </main>
  );
}
