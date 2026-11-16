import { useMemo, useState } from "react";
import type { StudioActivityDefinitionImplementationEditorProps, StudioActivityDiagnosticFocusResult } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { ActivityOutputContract } from "./activityDefinitionTypes";
import { useWorkflowActivities } from "./api/activityDesign";
import { activityGraphDocumentAdapter } from "./activityGraphDocumentAdapter";
import { getActivityGraphOutputSetters } from "./activityGraphOutputSources";
import { ActivityGraphOutputMappingEditor, normalizeActivityGraphPayload, useActivityGraphExpressionDescriptors, toWorkflowInput, toVisibleGraphVariable, toWorkflowVariables } from "./ActivityGraphImplementationEditor";
import { indexActivityPresentation, resolveActivityLabel } from "./activityPresentation";
import { WorkflowReferenceAuthoringProvider } from "./workflowReferenceAuthoring";

export function ActivityGraphOutputValueEditor({ output, onRevealStep, ...props }: StudioActivityDefinitionImplementationEditorProps & {
  output: ActivityOutputContract;
  onRevealStep(nodeId: string): Promise<StudioActivityDiagnosticFocusResult>;
}) {
  const { context, contract, value, expressionEditors = [], readOnly, onChange } = props;
  const catalogQuery = useWorkflowActivities(context);
  const catalog = useMemo(() => new Map((catalogQuery.data?.activities ?? []).map(item => [item.activityVersionId, item])), [catalogQuery.data?.activities]);
  const root = activityGraphDocumentAdapter.readRoot(value);
  const payload = normalizeActivityGraphPayload(value.payload);
  const setters = getActivityGraphOutputSetters(root, catalog);
  const assigned = setters.filter(setter => setter.outputName === output.name);
  const orphaned = setters.filter(setter => setter.outputName === null || !(contract?.outputs ?? []).some(item => item.name === setter.outputName));
  const mapping = payload.outputMappings.find(item => item.outputReferenceKey === output.referenceKey);
  const expressions = useActivityGraphExpressionDescriptors(context);
  const [changingSource, setChangingSource] = useState(false);
  const presentation = indexActivityPresentation(value.activityPresentation);
  const label = (nodeId: string) => {
    const name = resolveActivityLabel(presentation.get(nodeId), undefined, "Set Output");
    return assigned.length + orphaned.length > 1 ? `${name} ${setters.findIndex(setter => setter.nodeId === nodeId) + 1}` : name;
  };
  const workflowInputs = (contract?.inputs ?? []).map(toWorkflowInput);
  const visibleVariables = payload.variables.flatMap(toVisibleGraphVariable);
  const workflowState = { inputs: workflowInputs, outputs: contract?.outputs ?? [], rootActivity: root, variables: toWorkflowVariables(payload.variables) };
  const stepList = (steps: typeof assigned) => <ul className="ad-output-setters">{steps.map(setter => <li key={setter.nodeId}>
    {steps.length > 1 ? <span>{label(setter.nodeId)}</span> : null}
    <StudioButton size="sm" onClick={() => void onRevealStep(setter.nodeId)} aria-label={assigned.length + orphaned.length > 1 ? `Go to step ${label(setter.nodeId)}` : "Go to step"}>Go to step</StudioButton>
  </li>)}</ul>;

  return <section className="ad-output-value-source wf-tokens" data-output-reference-key={output.referenceKey} tabIndex={-1} aria-label={`Value source for ${output.name}`}>
    <header><h4>Value source</h4>{mapping || assigned.length ? <StudioButton variant="ghost" size="sm" disabled={readOnly} aria-expanded={changingSource} onClick={() => setChangingSource(current => !current)}>Change value source</StudioButton> : null}</header>
    {assigned.length ? <>
      <p>{assigned.length === 1 ? "Assigned by Set Output in the designer." : `Assigned by ${assigned.length} Set Output steps in the designer.`}</p>
      {stepList(assigned)}
      {changingSource ? <div className="ad-output-source-guidance" role="status"><p>Open {assigned.length === 1 ? "the step" : "each step"} and remove it or select a different output before choosing a value here. Your graph stays unchanged until you edit it.</p><StudioButton size="sm" disabled>Choose value</StudioButton></div> : null}
    </> : null}
    {orphaned.length ? <div className="ad-output-source-guidance" role="alert"><p>{orphaned.length === 1 ? "A Set Output step targets" : `${orphaned.length} Set Output steps target`} a removed, renamed or missing output. Open {orphaned.length === 1 ? "the step" : "the steps"} to select a current output.</p>{stepList(orphaned)}</div> : null}
    {mapping || assigned.length === 0 ? <WorkflowReferenceAuthoringProvider inputBindingTarget="referenceKey" workflowState={workflowState} workflowInputs={workflowInputs} visibleVariables={visibleVariables} status="ready">
      {mapping && changingSource && assigned.length === 0 ? <p className="wf-muted">Choosing a different expression type replaces the current value. Remove the connection to leave this output unassigned.</p> : null}
      <ActivityGraphOutputMappingEditor output={output} mapping={mapping} assignedBySetOutput={assigned.length > 0} embedded preserveValue={assigned.length > 0} changingSource={changingSource || assigned.length > 0} expressionDescriptors={expressions.descriptors} expressionDescriptorStatus={expressions.status} expressionEditors={expressionEditors} root={root} readOnly={readOnly} onChange={source => {
        if (readOnly || assigned.length > 0 && source !== null) return;
        const retained = payload.outputMappings.filter(item => item.outputReferenceKey !== output.referenceKey);
        onChange({ ...value, payload: { ...(value.payload as Record<string, unknown>), outputMappings: source ? [...retained, { outputReferenceKey: output.referenceKey, source }] : retained } });
        if (source === null) setChangingSource(false);
      }} />
    </WorkflowReferenceAuthoringProvider> : null}
  </section>;
}
