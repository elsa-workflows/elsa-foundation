import { useId, useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import type { StudioActivityDescriptor, StudioActivityOutputDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, VisibleVariableView } from "./workflowTypes";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import { formatTypeName } from "./activityProperties";
import { conversionModeDescriptors, describeInferredSource, isDefaultConversion, readConversionMode } from "./conversionSettings";
import { ConversionControl, useConversionProfiles } from "./ActivityPropertiesPanel";
import { getOutputPropertyName, readOutputCapture, withCaptureConversion, writeOutputCapture, type ActivityOutputCapture } from "./activityOutputCapture";
import { makeVariableReference } from "./scopedVariables";
import { selectedOptionKey, VariableTargetSelect } from "./variableTargetPicker";

/**
 * Inspector Outputs editor. An activity output is produced, not authored, but the author may capture its
 * value into a workflow variable (v1: workflow-scope targets only). Each browsable output renders its
 * name/type/description plus a scope-aware variable-target picker ("Capture into…") and an optional
 * conversion control; an empty selection clears the capture. The capture rides the node as a top-level
 * property folded into `node.outputs[]` by the wire layer. The section stays quiet when nothing is bound.
 */
export function ActivityOutputsPanel({
  descriptor,
  activity,
  context,
  visibleVariables,
  scopeStatus,
  scopeRetry,
  onChange
}: {
  descriptor: StudioActivityDescriptor | null;
  activity: ActivityNode | null;
  context: StudioEndpointContext;
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  scopeRetry?: () => void;
  onChange(activity: ActivityNode): void;
}) {
  const conversionProfiles = useConversionProfiles(context);
  if (!descriptor || !activity) return null;

  const outputs = descriptor.outputs.filter(output => output.isBrowsable !== false);
  if (outputs.length === 0) return null;

  return (
    <div className="wf-outputs">
      <span className="wf-section-label">Outputs</span>
      {outputs.map(output => (
        <OutputCaptureRow
          key={getOutputPropertyName(output)}
          output={output}
          capture={readOutputCapture(activity, output)}
          visibleVariables={visibleVariables}
          scopeStatus={scopeStatus}
          scopeRetry={scopeRetry}
          conversionProfiles={conversionProfiles}
          onChange={capture => onChange(writeOutputCapture(activity, output, capture))}
        />
      ))}
    </div>
  );
}

function OutputCaptureRow({
  output,
  capture,
  visibleVariables,
  scopeStatus,
  scopeRetry,
  conversionProfiles,
  onChange
}: {
  output: StudioActivityOutputDescriptor;
  capture: ActivityOutputCapture | null;
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  scopeRetry?: () => void;
  conversionProfiles: ReturnType<typeof useConversionProfiles>;
  onChange(capture: ActivityOutputCapture | null): void;
}) {
  const labelId = useId();
  const conversionRegionId = useId();
  const [conversionOpen, setConversionOpen] = useState(false);
  const label = output.displayName || output.name;

  const conversionAuthored = capture ? !isDefaultConversion(capture.conversion) : false;
  const conversionMode = readConversionMode(capture?.conversion);
  const conversionModeDisplayName =
    conversionModeDescriptors.find(descriptor => descriptor.mode === conversionMode)?.displayName ?? "Auto";
  const conversionCaption = capture
    ? `${describeInferredSource("Variable", capture.target, conversionMode)} → ${formatTypeName(output.typeName)}`
    : "";

  // Reuse ConversionControl by presenting the capture as a wrapped Variable-expression value; its onChange
  // returns the wrapped shape, so map its `conversion` back onto the capture.
  const wrappedForConversion = capture
    ? { typeName: output.typeName, expression: { type: "Variable", value: capture.target }, conversion: capture.conversion }
    : null;

  return (
    <div className="wf-output-row">
      <div className="wf-output-row-header">
        <span className="wf-output-name" id={labelId}>{label}</span>
        <div className="wf-output-row-header-meta">
          <span className="wf-output-type">{formatTypeName(output.typeName)}</span>
          {capture ? (
            <button
              type="button"
              className={conversionAuthored ? "wf-conversion-toggle authored" : "wf-conversion-toggle"}
              aria-label={`${label} conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              title={`Conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              aria-expanded={conversionOpen}
              aria-controls={conversionRegionId}
              onClick={() => setConversionOpen(open => !open)}
            >
              <SlidersHorizontal size={13} />
            </button>
          ) : null}
        </div>
      </div>
      {output.description ? <p className="wf-output-description">{output.description}</p> : null}
      <div className="wf-output-capture">
        <span className="wf-output-capture-label" aria-hidden="true">Capture into</span>
        <VariableTargetSelect
          ariaLabelledBy={labelId}
          ariaLabel={`Capture ${label} into a variable`}
          visibleVariables={visibleVariables}
          status={scopeStatus}
          retry={scopeRetry}
          selected={selectedOptionKey(capture?.target ?? null)}
          workflowScopeOnly
          onSelect={selection => {
            if (!selection) {
              onChange(null);
              return;
            }
            const target = makeVariableReference(selection.referenceKey, selection.scopeId);
            onChange(capture ? { ...capture, target } : { target });
          }}
        />
      </div>
      {capture && wrappedForConversion && (conversionOpen || conversionAuthored) ? (
        <div id={conversionRegionId} className="wf-conversion-region">
          {conversionOpen ? (
            <ConversionControl
              inputLabel={label}
              targetTypeName={output.typeName}
              wrapped={wrappedForConversion}
              profiles={conversionProfiles}
              disabled={false}
              onChange={next => onChange(withCaptureConversion(capture, next.conversion ?? null))}
            />
          ) : (
            <button
              type="button"
              className="wf-conversion-chip"
              aria-label={`Edit ${label} conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              onClick={() => setConversionOpen(true)}
            >
              {conversionModeDisplayName} · {conversionCaption}
            </button>
          )}
        </div>
      ) : null}
    </div>
  );
}
