import type React from "react";
import type { Edge } from "@xyflow/react";
import type { StudioActivityDescriptor, StudioWorkflowContextConnection } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDefinitionVersionDetails, WorkflowDraft, WorkflowInstanceDetails, WorkflowTestRunView } from "../workflowTypes";
import type { getChildSlots, ScopeFrame, WorkflowEdgeData } from "../workflowAdapter";

export type CreateWorkflowKind = "sequence" | "flowchart" | "bpmn";

export interface CreateWorkflowDraft {
  name: string;
  description: string;
  rootActivityVersionId: string | null;
}

export const createWorkflowRootOptions: { value: CreateWorkflowKind; label: string; hint: string }[] = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." },
  { value: "bpmn", label: "BPMN", hint: "BPMN 2.0 process with events, gateways, and tasks." }
];

export interface ActivityPaletteGroup {
  category: string;
  activities: ActivityCatalogItem[];
}

export type CanvasView = "designer" | "code" | "properties";

// A structured editor error so banners can surface ProblemDetails `detail`, a correlatable `traceId`,
// and the HTTP status alongside the human-readable message. Plain strings are still accepted anywhere a
// `WorkflowErrorInput` is expected and are normalized into `{ message }`.
export interface WorkflowEditorError {
  message: string;
  detail?: string;
  traceId?: string;
  status?: number;
  code?: string;
}

export type WorkflowErrorInput = string | WorkflowEditorError;

export type SetWorkflowError = (value: WorkflowErrorInput) => void;

export type WorkflowEdge = Edge<WorkflowEdgeData>;

export type WorkflowEditorOperation = "idle" | "saving" | "promoting" | "publicationPreflight" | "publishing" | "testRunPreparing" | "testRunStarting";

export interface WorkflowTestRunState {
  draftSignature: string;
  view: WorkflowTestRunView;
}

export interface ExecutableRunState {
  artifactId: string;
  workflowExecutionId: string | null;
}

export type WorkflowConnectSource = { nodeId: string; handleId: string | null };

export type ConnectMenuState =
  | { kind: "fromPort"; sourceNodeId: string; sourceHandleId: string | null; clientX: number; clientY: number }
  | { kind: "spliceEdge"; edgeId: string; clientX: number; clientY: number }
  | { kind: "fromEmpty"; clientX: number; clientY: number };

export type WorkflowGraphConnection = StudioWorkflowContextConnection;

export type WorkflowNodeAvailabilityLookup = (input: { activityVersionId?: string | null; activityTypeKey?: string | null }) => ActivityAvailabilityDiagnosticEntry | null;

export interface WorkflowMetadataSuggestion {
  name?: string;
  description?: string;
}

export type WorkflowSidePanel = "palette" | "inspector";

export interface WorkflowEditorPanelTab {
  id: string;
  title: string;
  order: number;
  icon: React.ReactNode;
  render(): React.ReactNode;
}

export interface WorkflowDesignerPanelContext {
  definition: WorkflowDefinitionDetails["definition"];
  draft: WorkflowDraft;
  selectedActivity: ActivityNode | null;
  selectedActivityDescriptor: StudioActivityDescriptor | null;
  selectedActivitySlots: ReturnType<typeof getChildSlots>;
  // The inspector's view: the selected activity, or — with nothing selected — the scope OWNER
  // (the container whose canvas is displayed). Panels mirroring the inspector should render from
  // these; `selected*` stays strictly selection-based.
  inspectedActivity: ActivityNode | null;
  inspectedActivityDescriptor: StudioActivityDescriptor | null;
  inspectedActivitySlots: ReturnType<typeof getChildSlots>;
  // True only on the owner FALLBACK — nothing is selected and the owner is shown. NOT "the inspected
  // activity is the scope owner": when an owner is itself explicitly selected (possible for
  // unsupported designers) this is false even though `inspectedActivity === currentScopeOwner`.
  inspectedIsScopeOwner: boolean;
  catalog: ActivityCatalogItem[];
  currentScopeOwner: ActivityNode | null;
  frames: ScopeFrame[];
}

export type InstanceInspectorTab = "timeline" | "activity" | "issues" | "details";

export interface WorkflowInstanceInspectionData {
  details: WorkflowInstanceDetails;
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
}
