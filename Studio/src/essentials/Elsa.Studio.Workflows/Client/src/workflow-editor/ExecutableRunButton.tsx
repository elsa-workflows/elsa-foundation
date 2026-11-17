import { StudioButton, type StudioButtonVariant } from "@elsa-workflows/studio-ui";
import { Play } from "lucide-react";
import type { ExecutableWorkflowRunTarget } from "./useExecutableWorkflowRun";

export function ExecutableRunButton({
  target,
  runningArtifactId,
  ariaLabel,
  iconSize = 13,
  variant = "default",
  onRequest
}: {
  target: ExecutableWorkflowRunTarget | null;
  runningArtifactId: string | null;
  ariaLabel?: string;
  iconSize?: number;
  variant?: StudioButtonVariant;
  onRequest(target: ExecutableWorkflowRunTarget): Promise<void>;
}) {
  return (
    <StudioButton
      type="button"
      variant={variant}
      aria-label={ariaLabel}
      disabled={!target || Boolean(runningArtifactId)}
      title={target ? undefined : "No live published reference is available to run."}
      onClick={() => { if (target) void onRequest(target); }}
    >
      <Play size={iconSize} /> {target && runningArtifactId === target.artifactId ? "Running..." : "Run"}
    </StudioButton>
  );
}
