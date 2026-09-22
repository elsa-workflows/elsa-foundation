import { PackageCheck } from "lucide-react";
import { StudioAlert } from "../../ui";
import type { BuildArtifact, BuildResult, ExtensionBuilderCapabilities, PackagePromotionResult } from "../extensionBuilderApi";
import { isBuildSucceeded, promotionMessage } from "./helpers";

export function PromotePanel({
  capabilities,
  activeBuild,
  artifact,
  promotionResult,
  busy,
  canPromote,
  isBuildCurrent,
  onPromote
}: {
  capabilities: ExtensionBuilderCapabilities;
  activeBuild: BuildResult | null;
  artifact: BuildArtifact | null;
  promotionResult: PackagePromotionResult | null;
  busy: boolean;
  canPromote: boolean;
  isBuildCurrent: boolean;
  onPromote(): void;
}) {
  const promoteReason = !capabilities.canPromote
    ? "Requires canPromote"
    : !isBuildSucceeded(activeBuild)
      ? "A succeeded build is required"
      : !artifact
        ? "A build artifact is required"
        : !isBuildCurrent
          ? "Build artifact is stale. Rebuild the current source revision before promoting."
          : undefined;

  return (
    <div className="modules-inspector-section">
      <h4>Artifact</h4>
      {artifact ? (
        <dl className="modules-metadata">
          <div><dt>Package</dt><dd>{artifact.packageId}</dd></div>
          <div><dt>Version</dt><dd>{artifact.version}</dd></div>
          <div><dt>File</dt><dd>{artifact.fileName ?? artifact.id}</dd></div>
          <div><dt>Size</dt><dd>{artifact.size ? `${artifact.size} B` : "n/a"}</dd></div>
        </dl>
      ) : <p className="modules-muted">Promote becomes available after a successful build produces a package artifact.</p>}
      <button type="button" className="studio-button" disabled={busy || !canPromote} title={promoteReason} onClick={onPromote}>
        <PackageCheck size={15} />
        Promote build
      </button>
      {promotionResult ? (
        <StudioAlert tone={promotionResult.accepted ? "success" : "warning"}>
          {promotionMessage(promotionResult)}
        </StudioAlert>
      ) : null}
    </div>
  );
}
