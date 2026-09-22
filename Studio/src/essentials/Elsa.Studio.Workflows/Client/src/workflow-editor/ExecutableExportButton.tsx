import { PackageOpen } from "lucide-react";
import type { WorkflowExecutableSummary } from "../workflowTypes";

/**
 * Row action that downloads one artifact's compiled closure (studio #493). Mirrors
 * {@link ExecutableRunButton}: the list owns the in-flight artifact id, so every row disables while any
 * export runs and only the clicked row reports progress.
 */
export function ExecutableExportButton({
  executable,
  exportingArtifactId,
  ariaLabel,
  iconSize = 13,
  onRequest
}: {
  executable: WorkflowExecutableSummary;
  exportingArtifactId: string | null;
  ariaLabel?: string;
  iconSize?: number;
  onRequest(executable: WorkflowExecutableSummary): Promise<void>;
}) {
  const exportable = !!executable.definitionVersionId;
  return (
    <button
      type="button"
      aria-label={ariaLabel}
      disabled={!exportable || Boolean(exportingArtifactId)}
      title={exportable
        ? "Download the compiled executable artifact for a runtime engine"
        : "This artifact carries no definition version to export."}
      onClick={() => { if (exportable) void onRequest(executable); }}
    >
      <PackageOpen size={iconSize} /> {exportingArtifactId === executable.artifactId ? "Exporting…" : "Export"}
    </button>
  );
}
