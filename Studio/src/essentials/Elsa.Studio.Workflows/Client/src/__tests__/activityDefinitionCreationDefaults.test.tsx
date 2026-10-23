import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { CreateActivityDefinitionRequest } from "../activityDefinitionTypes";
import type { ActivityCatalogItem } from "../workflowTypes";
import { ActivityDefinitionCreateDialog } from "../ActivityDefinitionCreateDialog";
import { flowchartActivity } from "./fixtures/workflowCatalog";
import { testApi } from "./routeRenderingHelpers";

const api = vi.hoisted(() => ({
  catalog: { activities: [] as ActivityCatalogItem[] },
  capabilities: {
    contractSchemaVersions: [1],
    providers: [{ providerKey: "elsa.activity-graph", displayName: "Activity Graph", requiredOutcomes: [],
      manifestSchemas: [{ schemaVersion: "2", isAuthorable: true, migratableFromSchemaVersions: [] }] }]
  },
  create: vi.fn<(context: StudioEndpointContext, request: CreateActivityDefinitionRequest) => Promise<unknown>>(async () => ({}))
}));
vi.mock("../api/activityDesign", () => ({
  useActivityAuthoringCapabilities: () => ({ data: api.capabilities }),
  useWorkflowActivities: () => ({ data: api.catalog }),
  createActivityDefinition: api.create
}));
const contribution: StudioActivityDefinitionImplementationEditorContribution = {
  id: "graph", providerKey: "elsa.activity-graph", providerSchemaVersion: "2",
  createInitialImplementation: () => ({ providerKey: "elsa.activity-graph", providerSchemaVersion: "2", payload: { rootActivity: null, outcomeMappings: [] }, layout: [] }),
  component: () => null
};
let unmount: (() => void) | undefined;
afterEach(() => { unmount?.(); unmount = undefined; api.create.mockClear(); });

it.each([false, true])("selects and submits Sequence when Flowchart is missing or unavailable (%s)", async unavailableFlowchart => {
  const sequence = { ...flowchartActivity(), activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Activities.Sequence", displayName: "Sequence",
    authoringTemplate: { ...flowchartActivity().authoringTemplate!, activityVersionId: "sequence-v1", structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [] } } } };
  api.catalog = { activities: [...(unavailableFlowchart ? [{ ...flowchartActivity(), available: false }] : []), sequence] };
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  unmount = () => { flushSync(() => root.unmount()); container.remove(); };
  flushSync(() => root.render(<ActivityDefinitionCreateDialog context={testApi().backend} activityEditors={[contribution]} onClose={vi.fn()} onCreated={vi.fn()} />));
  const name = container.querySelector<HTMLInputElement>('[aria-label="Display name"]') ?? container.querySelector<HTMLInputElement>("input");
  const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
  flushSync(() => { setValue.call(name, "Reusable sequence"); name!.dispatchEvent(new Event("input", { bubbles: true })); });
  expect(container.querySelector<HTMLInputElement>('[aria-label="Sequence"]')?.checked).toBe(true);
  expect(container.querySelector<HTMLInputElement>('[aria-label="Flowchart"]')?.disabled).toBe(true);
  const create = [...container.querySelectorAll("button")].find(button => button.textContent === "Create definition")!;
  await vi.waitFor(() => expect(create.disabled).toBe(false));
  flushSync(() => create.click());
  await vi.waitFor(() => expect(api.create).toHaveBeenCalledOnce());
  expect(api.create.mock.calls[0][1].provider.payload).toMatchObject({ rootActivity: { activityVersionId: "sequence-v1" } });
});
