import { Play } from "lucide-react";
import type { ExecutableWorkflowRunTarget } from "./useExecutableWorkflowRun";

export function ExecutableRunButton({
  target,
  runningArtifactId,
  ariaLabel,
  iconSize = 13,
  onRequest
}: {
  target: ExecutableWorkflowRunTarget | null;
  runningArtifactId: string | null;
  ariaLabel?: string;
  iconSize?: number;
  onRequest(target: ExecutableWorkflowRunTarget): Promise<void>;
}) {
  return (
    <button
      type="button"
      aria-label={ariaLabel}
      disabled={!target || Boolean(runningArtifactId)}
      title={target ? undefined : "No live published reference is available to run."}
      onClick={() => { if (target) void onRequest(target); }}
    >
      <Play size={iconSize} /> {target && runningArtifactId === target.artifactId ? "Running..." : "Run"}
    </button>
  );
}
