import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { evaluateBackendRequirements } from "@elsa-workflows/studio-sdk";
import { EtlDefinitionsPage } from "./EtlDefinitionsPage";
import { EtlRunsPage } from "./EtlRunsPage";
import { EtlPublishedSelectionEditor } from "./EtlPublishedSelectionEditor";
import "./styles.css";

export function register(api: Pick<ElsaStudioModuleApi, "featureAreas" | "propertyEditors" | "backend" | "dialogs" | "backendSession">) {
  const design = [{id:"elsa.api.etl-design-v2",contractVersion:"1"}];
  const runtime = [{id:"elsa.api.etl-runtime",contractVersion:"1"}];
  const authoringAvailable = evaluateBackendRequirements(api.backendSession,design).state === "available";
  api.featureAreas.add({
    id: "etl", title: "ETL pipelines", navGroup: "Data", order: 55,
    description: "Author finite HTTP and file pipelines and inspect their durable execution.",
    ownedPaths: ["/etl/definitions", "/etl/connectors", "/etl/runs"], required: false, defaultEnabled: true,
    nav: {
      title: "ETL pipelines", path: authoringAvailable ? "/etl/definitions" : "/etl/runs", placement: "more", order: 35,
      requiresBackend:authoringAvailable ? design : runtime,
      items: [
        { title: "File definitions", path: "/etl/definitions", requiresBackend:design },
        { title: "Connector definitions", path: "/etl/connectors", requiresBackend:design },
        { title: "Runs", path: "/etl/runs", requiresBackend:runtime }
      ]
    },
    routes: [
      { id: "etl-definitions", path: "/etl/definitions", label: "ETL definitions", requiresBackend:design,
        component: () => <EtlDefinitionsPage context={api.backend} dialogs={api.dialogs} /> },
      { id: "etl-connectors", path: "/etl/connectors", label: "Connector pipelines", requiresBackend:design,
        component: () => <EtlDefinitionsPage context={api.backend} dialogs={api.dialogs} profile="connector" /> },
      { id: "etl-runs", path: "/etl/runs", label: "ETL runs", requiresBackend:runtime,
        component: () => <EtlRunsPage context={api.backend} dialogs={api.dialogs} /> }
    ]
  });
  api.propertyEditors.add({
    id: "elsa.etl-published-selection", order: 100,
    requiresBackend:design,
    supports: (descriptor, context) => descriptor.name === "Pipeline"
      && descriptor.uiHint === "etl-published-selection" && context.scope !== "collection",
    component: props => <EtlPublishedSelectionEditor {...props} endpointContext={api.backend} />
  });
}
