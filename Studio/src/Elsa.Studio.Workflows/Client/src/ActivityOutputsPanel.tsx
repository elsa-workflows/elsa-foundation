import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { formatTypeName } from "./activityProperties";

// Read-only companion to ActivityPropertiesPanel: activity outputs are produced, not authored, so they
// render as a plain labelled list (name · type · description) rather than editors. The descriptor already
// carries `outputs` end-to-end; this is the only place that surfaces them.
export function ActivityOutputsPanel({ descriptor }: { descriptor: StudioActivityDescriptor | null }) {
  if (!descriptor) return null;

  const outputs = descriptor.outputs.filter(output => output.isBrowsable !== false);
  if (outputs.length === 0) return null;

  return (
    <div className="wf-outputs">
      <span className="wf-section-label">Outputs</span>
      {outputs.map(output => (
        <div key={output.name} className="wf-output-row">
          <div className="wf-output-row-header">
            <span className="wf-output-name">{output.displayName || output.name}</span>
            <span className="wf-output-type">{formatTypeName(output.typeName)}</span>
          </div>
          {output.description ? <p className="wf-output-description">{output.description}</p> : null}
        </div>
      ))}
    </div>
  );
}
