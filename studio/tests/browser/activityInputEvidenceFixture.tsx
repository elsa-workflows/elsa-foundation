import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowActivityExecutionDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import type { ExecutableGraphNodeFacts } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/executableGraph";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const activity: ActivityExecutionStateSummary = {
  activityExecutionId: "renewal-execution", workflowExecutionId: "renewal-run",
  executableNodeId: "renewal", authoredActivityId: "renewal",
  activityType: "Contoso.RegisterRenewal", activityTypeVersion: "1.0.0",
  status: "Completed", subStatus: null,
  scheduledAt: "2026-10-04T10:00:00Z", startedAt: "2026-10-04T10:00:00Z", completedAt: "2026-10-04T10:00:01Z",
  bookmarkIds: [], incidentIds: [], faultCount: 0, aggregateFaultCount: 0, metadata: {}
};
const inputs = [
  { referenceKey: "policy", name: "PolicyReference", displayName: "Policy reference", typeName: "System.String" },
  { referenceKey: "premium", name: "Premium", typeName: "System.Decimal" },
  { referenceKey: "customer", name: "Customer", typeName: "Contoso.Customer" },
  { referenceKey: "notes", name: "Notes", typeName: "System.String" },
  { referenceKey: "token", name: "Token", typeName: "System.String", isSensitive: true },
  { referenceKey: "discount", name: "Discount", typeName: "System.Decimal" }
];
const catalog: ActivityCatalogItem[] = [{
  activityVersionId: "renewal-v1", activityTypeKey: activity.activityType, version: "1.0.0",
  category: "Policies", displayName: "Register renewal", description: null, executionType: "Action",
  inputs, outputs: [], designFacets: []
}];
const sources = [
  { inputKey: "policy", expressionType: "Literal", value: "POL-1042" },
  { inputKey: "premium", expressionType: "JavaScript", value: "variables.annualPremium * 1.05" },
  { inputKey: "customer", expressionType: "Variable", value: "customer" },
  { inputKey: "notes", expressionType: "Literal", value: "" },
  { inputKey: "token", expressionType: "Literal", value: "HIDDEN_SOURCE_VALUE", isSensitive: true },
  { inputKey: "discount", expressionType: "JavaScript", value: "variables.discount.rate" }
];
const facts: ExecutableGraphNodeFacts = {
  executableNodeId: "renewal", authoredActivityId: "renewal", activityType: activity.activityType,
  activityTypeVersion: "1.0.0", structureKind: null, available: true, outputCaptures: [],
  authoredInputsAccess: "visible",
  authoredInputs: sources.map(source => ({ ...source, executableNodeId: "renewal" })),
  inputBindings: sources.map(source => ({ inputKey: source.inputKey, inputName: inputs.find(input => input.referenceKey === source.inputKey)!.name, source: source.expressionType, literal: source.value, isSensitive: source.isSensitive }))
};

export function ActivityInputEvidenceFixture({ context }: { context: StudioEndpointContext }) {
  return <WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} executableNodeFacts={facts} />;
}
