import { useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listPublicationSlots } from "../api/publishing";
import type { WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";

// Resolves the draft-equivalence signal for the latest test run: the published executable of this
// definition that shares the test run's artifact id, when one exists. Uses the imperative load pattern
// the designer tree relies on (no QueryClientProvider there). The signal is advisory — a failed
// executables load resolves to "no signal", never to an error surface.
export function useDraftEquivalence(
  context: StudioEndpointContext,
  definitionId: string,
  testRun: WorkflowTestRunView | null
) {
  const artifactId = testRun?.artifactId ?? null;
  const [equivalent, setEquivalent] = useState<WorkflowExecutableSummary | null>(null);

  useEffect(() => {
    setEquivalent(null);
    if (!artifactId) return;

    let cancelled = false;
    Promise.all([listExecutables(context, { scope: "all", includeRetired: true }), listPublicationSlots(context, definitionId)]).then(
      ([executables, slots]) => {
        const publication = slots.map(slot => slot.publication).find(candidate =>
          candidate?.artifactId === artifactId && candidate.status === "active");
        const executable = publication ? executables.find(candidate => candidate.artifactId === publication.artifactId) : null;
        if (!cancelled) setEquivalent(executable ? {
          ...executable,
          definitionId,
          definitionVersionId: publication?.versionId ?? "",
          publishedAt: publication?.activatedAt ?? null
        } : null);
      },
      () => { if (!cancelled) setEquivalent(null); }
    );

    return () => { cancelled = true; };
  }, [context, definitionId, artifactId]);

  return equivalent;
}
