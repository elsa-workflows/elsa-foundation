import { useEffect, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier } from "@elsa-workflows/studio-ui";
import { getDefinition } from "../api/workflowDesign";

/** Optional Design metadata never supplies serving contracts or invocation authority. */
export function useWorkflowName(api: ElsaStudioModuleApi, scope: string, definitionId?: string) {
  const [resolved, setResolved] = useState<{ context: ElsaStudioModuleApi["backend"]; scope: string; id: string; name?: string }>();
  useEffect(() => {
    if (!definitionId) return;
    const context = api.backend, controller = new AbortController();
    void getDefinition(context, definitionId, controller.signal).then(details => {
      if (!controller.signal.aborted) setResolved({ context, scope, id: definitionId,
        name: details.definition.id === definitionId ? details.definition.name?.trim() || undefined : undefined });
    }, () => { if (!controller.signal.aborted) setResolved({ context, scope, id: definitionId }); });
    return () => controller.abort();
  }, [api.backend, definitionId, scope]);
  return resolved?.context === api.backend && resolved.scope === scope && resolved.id === definitionId ? resolved.name : undefined;
}

export function WorkflowIdentity({ api, scope, definitionId }: { api: ElsaStudioModuleApi; scope: string; definitionId: string }) {
  const name = useWorkflowName(api, scope, definitionId);
  return <><span>{name || definitionId}</span><CopyableIdentifier label="Workflow definition ID" value={definitionId} /></>;
}
