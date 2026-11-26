import { AlertTriangle } from "lucide-react";
import { findHttpEndpointResponseWarning } from "./httpEndpointResponseGuidance";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

/** A design-time advisory shown beside an HTTP Endpoint's editable inputs. */
export function HttpEndpointResponseWarning({
  rootActivity,
  endpointNodeId,
  catalog,
  onUseSync
}: {
  rootActivity: ActivityNode | null | undefined;
  endpointNodeId: string;
  catalog: ActivityCatalogItem[];
  onUseSync(): void;
}) {
  const warning = findHttpEndpointResponseWarning(rootActivity, endpointNodeId, catalog);
  if (!warning) return null;

  return (
    <aside className="wf-http-response-warning" role="note" aria-label="HTTP response behavior">
      <AlertTriangle size={15} aria-hidden="true" />
      <div>
        <p>This response will be stored; it won’t be returned to the caller.</p>
        <button type="button" className="wf-link-button" onClick={onUseSync}>
          Switch to Wait for workflow response
        </button>
      </div>
    </aside>
  );
}
