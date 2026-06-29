import { RotateCcw } from "lucide-react";
import type { RuntimeVersion } from "../extensionBuilderApi";
import { formatDate } from "./helpers";

export function RuntimeVersionRow({
  version,
  busy,
  canRollback,
  currentVersion,
  onRollback
}: {
  version: RuntimeVersion;
  busy: boolean;
  canRollback: boolean;
  currentVersion?: string | null;
  onRollback(version: string): void;
}) {
  const disabled = busy || !canRollback || !version.available || version.version === currentVersion;
  return (
    <div className="modules-list-row">
      <span>
        <strong>{version.version}</strong>
        <small>{version.available ? formatDate(version.promotedAt) : "Rollback target unavailable"}</small>
      </span>
      <button type="button" className="studio-button" disabled={disabled} title={!canRollback ? "Requires canRollback" : !version.available ? "Rollback target is unavailable" : undefined} onClick={() => onRollback(version.version)}>
        <RotateCcw size={15} />
        Rollback
      </button>
    </div>
  );
}
