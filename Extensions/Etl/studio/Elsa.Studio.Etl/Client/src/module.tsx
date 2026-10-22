import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { EtlDefinitionsPage } from "./EtlDefinitionsPage";
import { EtlRunsPage } from "./EtlRunsPage";
import { EtlPublishedSelectionEditor } from "./EtlPublishedSelectionEditor";
import "./styles.css";

export function register(api: Pick<ElsaStudioModuleApi, "featureAreas" | "propertyEditors" | "backend" | "dialogs">) {
  api.featureAreas.add({
    id: "etl", title: "ETL pipelines", navGroup: "Data", order: 55,
    description: "Author finite HTTP and file pipelines and inspect their durable execution.",
    ownedPaths: ["/etl/definitions", "/etl/connectors", "/etl/runs"], required: false, defaultEnabled: true,
    nav: {
      title: "ETL pipelines", path: "/etl", placement: "more", order: 35,
      items: [
        { title: "File definitions", path: "/etl/definitions" },
        { title: "Connector definitions", path: "/etl/connectors" },
        { title: "Runs", path: "/etl/runs" }
      ]
    },
    routes: [
      { id: "etl-definitions", path: "/etl/definitions", label: "ETL definitions",
        component: () => <EtlDefinitionsPage context={api.backend} dialogs={api.dialogs} /> },
      { id: "etl-connectors", path: "/etl/connectors", label: "Connector pipelines",
        component: () => <EtlDefinitionsPage context={api.backend} dialogs={api.dialogs} profile="connector" /> },
      { id: "etl-runs", path: "/etl/runs", label: "ETL runs",
        component: () => <EtlRunsPage context={api.backend} dialogs={api.dialogs} /> }
    ]
  });
  api.propertyEditors.add({
    id: "elsa.etl-published-selection", order: 100,
    supports: (descriptor, context) => descriptor.name === "Pipeline"
      && descriptor.uiHint === "etl-published-selection" && context.scope !== "collection",
    component: props => <EtlPublishedSelectionEditor {...props} endpointContext={api.backend} />
  });
}
