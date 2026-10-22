import type { ComponentType } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import * as api from "./etlApi";
import { createDefaultEtlPipeline } from "./authoringHelpers";
import { createDefaultEtlConnectorPipeline } from "./connectorAuthoringHelpers";
import { EtlPipelineEditor } from "./components/EtlPipelineEditor";
import { EtlConnectorPipelineEditor } from "./components/EtlConnectorPipelineEditor";
import type {
  EtlBindingSummary, EtlConnectorBindingSummary, EtlCreateDefinitionRequest, EtlDiagnostic,
  EtlDraftView, EtlPage, EtlPageRequest, EtlPipeline, EtlPipelineV2, EtlPublishedArtifactView,
  EtlUpdateDraftRequest
} from "./etlTypes";

/** Each authoring profile supplies its wire contract and editor to the same revision-aware workspace. */
export interface EtlAuthoringProfile<TPipeline, TBinding extends EtlBindingSummary> {
  title: string;
  description: string;
  createDefault(): TPipeline;
  Editor: ComponentType<{
    pipeline: TPipeline;
    onChange(pipeline: TPipeline): void;
    bindings: readonly TBinding[];
    readOnly?: boolean;
    bindingsLoading?: boolean;
    bindingsUnavailable?: boolean;
    diagnostics?: readonly EtlDiagnostic[];
  }>;
  api: {
    listEtlBindings(context: StudioEndpointContext, request?: EtlPageRequest, signal?: AbortSignal): Promise<EtlPage<TBinding>>;
    listEtlDefinitions: typeof api.listEtlDefinitions;
    listEtlVersions: typeof api.listEtlVersions;
    getEtlDefinition(context: StudioEndpointContext, id: string, signal?: AbortSignal): Promise<EtlDraftView<TPipeline>>;
    createEtlDefinition(context: StudioEndpointContext, request: EtlCreateDefinitionRequest<TPipeline>, signal?: AbortSignal): Promise<EtlDraftView<TPipeline>>;
    updateEtlDraft(context: StudioEndpointContext, id: string, request: EtlUpdateDraftRequest<TPipeline>, signal?: AbortSignal): Promise<EtlDraftView<TPipeline>>;
    getEtlVersion(context: StudioEndpointContext, id: string, version: number, signal?: AbortSignal): Promise<EtlPublishedArtifactView<TPipeline>>;
    validateEtlDraft: typeof api.validateEtlDraft;
    publishEtlDefinition: typeof api.publishEtlDefinition;
  };
}

export const fileAuthoringProfile: EtlAuthoringProfile<EtlPipeline, EtlBindingSummary> = {
  title: "ETL definitions",
  description: "Create a finite file pipeline, validate a saved draft, and publish an immutable version.",
  createDefault: createDefaultEtlPipeline,
  Editor: EtlPipelineEditor,
  api: {
    listEtlBindings: api.listEtlBindings,
    listEtlDefinitions: api.listEtlDefinitions,
    listEtlVersions: api.listEtlVersions,
    getEtlDefinition: api.getEtlDefinition,
    createEtlDefinition: api.createEtlDefinition,
    updateEtlDraft: api.updateEtlDraft,
    getEtlVersion: api.getEtlVersion,
    validateEtlDraft: api.validateEtlDraft,
    publishEtlDefinition: api.publishEtlDefinition
  }
};

export const connectorAuthoringProfile: EtlAuthoringProfile<EtlPipelineV2, EtlConnectorBindingSummary> = {
  title: "Connector pipelines",
  description: "Read HTTP JSON or CSV, transform records, and deliver to HTTP or JSONL destinations.",
  createDefault: createDefaultEtlConnectorPipeline,
  Editor: EtlConnectorPipelineEditor,
  api: {
    listEtlBindings: api.listEtlConnectorBindings,
    listEtlDefinitions: api.listEtlConnectorDefinitions,
    listEtlVersions: api.listEtlConnectorVersions,
    getEtlDefinition: api.getEtlConnectorDefinition,
    createEtlDefinition: api.createEtlConnectorDefinition,
    updateEtlDraft: api.updateEtlConnectorDraft,
    getEtlVersion: api.getEtlConnectorVersion,
    validateEtlDraft: api.validateEtlConnectorDraft,
    publishEtlDefinition: api.publishEtlConnectorDefinition
  }
};
