import { useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/workflows";
import type { WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import { findPublishedEquivalent } from "./editorHelpers";

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
    listExecutables(context).then(
      executables => { if (!cancelled) setEquivalent(findPublishedEquivalent(artifactId, executables, definitionId)); },
      () => { if (!cancelled) setEquivalent(null); }
    );

    return () => { cancelled = true; };
  }, [context, definitionId, artifactId]);

  return equivalent;
}
